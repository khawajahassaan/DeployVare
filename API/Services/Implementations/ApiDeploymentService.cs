using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Cryptography;

namespace API.Services.Implementations;

public class ApiDeploymentService : IApiDeploymentService
{
    private readonly DeployVareDbContext _context;
    private readonly ISshService _sshService;
    private readonly IIisManagementService _iisService;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<ApiDeploymentService> _logger;
    private readonly IConfiguration _configuration;

    public ApiDeploymentService(
        DeployVareDbContext context,
        ISshService sshService,
        IIisManagementService iisService,
        IEncryptionService encryptionService,
        ILogger<ApiDeploymentService> logger,
        IConfiguration configuration)
    {
        _context = context;
        _sshService = sshService;
        _iisService = iisService;
        _encryptionService = encryptionService;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<ApiDeploymentResultDto> DeployAsync(DeployApiRequest request, int userId)
    {
        var stopwatch = Stopwatch.StartNew();
        var deploymentSteps = new List<string>();
        var startTime = DateTime.UtcNow;

        // Get server configuration
        var server = await _context.ApplicationServers
            .Include(s => s.Application)
            .FirstOrDefaultAsync(s => s.Id == request.ApplicationServerId);

        if (server == null)
        {
            return new ApiDeploymentResultDto
            {
                Status = ApiDeploymentStatus.Failed,
                Message = "Application server not found",
                ErrorMessage = "Server configuration does not exist",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            };
        }

        // Create deployment record
        var deployment = new ApiDeployment
        {
            ApplicationServerId = request.ApplicationServerId,
            DeployedBy = userId,
            Version = request.Version,
            Status = ApiDeploymentStatus.Pending,
            StartedAt = startTime
        };

        _context.ApiDeployments.Add(deployment);
        await _context.SaveChangesAsync();

        try
        {
            _logger.LogInformation($"Starting deployment {deployment.Id} to {server.ServerName} ({server.HostName})");

            // Step 1: Validate and save uploaded files
            deploymentSteps.Add("Validating and saving uploaded files...");
            var tempFiles = await SaveUploadedFilesAsync(request.Files, deployment.Id);
            deployment.Status = ApiDeploymentStatus.Pending;
            await _context.SaveChangesAsync();

            // Step 2: Stop IIS services
            deploymentSteps.Add($"Stopping IIS services on {server.ServerName}...");
            deployment.Status = ApiDeploymentStatus.StoppingServices;
            await _context.SaveChangesAsync();

            var (appPoolStopped, appPoolMsg) = await _iisService.StopAppPoolAsync(server);
            if (!appPoolStopped)
            {
                throw new Exception($"Failed to stop App Pool: {appPoolMsg}");
            }
            deploymentSteps.Add($"✓ App Pool stopped: {appPoolMsg}");

            var (websiteStopped, websiteMsg) = await _iisService.StopWebsiteAsync(server);
            if (!websiteStopped)
            {
                throw new Exception($"Failed to stop Website: {websiteMsg}");
            }
            deploymentSteps.Add($"✓ Website stopped: {websiteMsg}");

            // Step 3: Create backup
            deploymentSteps.Add("Creating backup of existing files...");
            deployment.Status = ApiDeploymentStatus.CreatingBackup;
            await _context.SaveChangesAsync();

            var (backupCreated, backupFileName, backupMsg) = await _iisService.CreateBackupAsync(server);
            if (!backupCreated)
            {
                throw new Exception($"Failed to create backup: {backupMsg}");
            }

            // Backup might be skipped if folder is empty (testing scenario)
            if (!string.IsNullOrEmpty(backupFileName))
            {
                deployment.BackupFileName = backupFileName;
                await _context.SaveChangesAsync();
                deploymentSteps.Add($"✓ Backup created: {backupFileName}");
            }
            else
            {
                deploymentSteps.Add($"⊘ Backup skipped: {backupMsg}");
            }

            // Step 4: Upload new files
            deploymentSteps.Add($"Uploading {tempFiles.Count} files...");
            deployment.Status = ApiDeploymentStatus.UploadingFiles;
            await _context.SaveChangesAsync();

            var uploadSuccess = await UploadFilesToServerAsync(server, tempFiles);
            if (!uploadSuccess)
            {
                throw new Exception("Failed to upload files to server");
            }
            deploymentSteps.Add($"✓ Uploaded {tempFiles.Count} files successfully");

            // Step 5: Start IIS services
            deploymentSteps.Add("Starting IIS services...");
            deployment.Status = ApiDeploymentStatus.StartingServices;
            await _context.SaveChangesAsync();

            var (websiteStarted, websiteStartMsg) = await _iisService.StartWebsiteAsync(server);
            if (!websiteStarted)
            {
                throw new Exception($"Failed to start Website: {websiteStartMsg}");
            }
            deploymentSteps.Add($"✓ Website started: {websiteStartMsg}");

            var (appPoolStarted, appPoolStartMsg) = await _iisService.StartAppPoolAsync(server);
            if (!appPoolStarted)
            {
                throw new Exception($"Failed to start App Pool: {appPoolStartMsg}");
            }
            deploymentSteps.Add($"✓ App Pool started: {appPoolStartMsg}");

            // Step 6: Verify deployment
            deploymentSteps.Add("Verifying deployment...");
            deployment.Status = ApiDeploymentStatus.Verifying;
            await _context.SaveChangesAsync();

            await Task.Delay(2000); // Wait 2 seconds for services to stabilize

            var (verified, verifyMsg) = await _iisService.VerifyDeploymentAsync(server);
            if (!verified)
            {
                _logger.LogWarning($"Deployment verification returned warning: {verifyMsg}");
                deploymentSteps.Add($"⚠ Verification warning: {verifyMsg}");
            }
            else
            {
                deploymentSteps.Add($"✓ Deployment verified successfully");
            }

            // Success!
            stopwatch.Stop();
            deployment.Status = ApiDeploymentStatus.Success;
            deployment.CompletedAt = DateTime.UtcNow;
            deployment.DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
            await _context.SaveChangesAsync();

            // Clean up temp files
            CleanupTempFiles(tempFiles);

            deploymentSteps.Add($"✓ Deployment completed successfully in {stopwatch.Elapsed.TotalSeconds:F1} seconds!");

            _logger.LogInformation($"Deployment {deployment.Id} completed successfully");

            return new ApiDeploymentResultDto
            {
                DeploymentId = deployment.Id,
                Status = ApiDeploymentStatus.Success,
                Message = "Deployment completed successfully",
                BackupFileName = backupFileName,
                DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds,
                DeploymentSteps = deploymentSteps,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                DeployedFiles = deployment.DeploymentFiles.Select(f => new ApiDeploymentFileDto
                {
                    Id = f.Id,
                    FileName = f.FileName,
                    FileSize = f.FileSize,
                    FileHash = f.FileHash,
                    UploadedAt = f.UploadedAt
                }).ToList()
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Deployment {deployment.Id} failed");

            deployment.Status = ApiDeploymentStatus.Failed;
            deployment.ErrorMessage = ex.Message;
            deployment.CompletedAt = DateTime.UtcNow;
            deployment.DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
            await _context.SaveChangesAsync();

            deploymentSteps.Add($"✗ Deployment failed: {ex.Message}");

            // Try to restart services if they were stopped
            try
            {
                _logger.LogInformation("Attempting to restart services after failure...");
                await _iisService.StartWebsiteAsync(server);
                await _iisService.StartAppPoolAsync(server);
                deploymentSteps.Add("Services restarted after failure");
            }
            catch (Exception restartEx)
            {
                _logger.LogError(restartEx, "Failed to restart services after deployment failure");
                deploymentSteps.Add($"Failed to restart services: {restartEx.Message}");
            }

            return new ApiDeploymentResultDto
            {
                DeploymentId = deployment.Id,
                Status = ApiDeploymentStatus.Failed,
                Message = "Deployment failed",
                ErrorMessage = ex.Message,
                DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds,
                DeploymentSteps = deploymentSteps,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            };
        }
    }

    public async Task<List<ApiDeploymentDto>> GetDeploymentHistoryAsync(int applicationServerId, int limit = 50)
    {
        var deployments = await _context.ApiDeployments
            .Include(d => d.ApplicationServer)
                .ThenInclude(s => s.Application)
            .Include(d => d.DeployedByUser)
            .Include(d => d.DeploymentFiles)
            .Where(d => d.ApplicationServerId == applicationServerId)
            .OrderByDescending(d => d.DeploymentDate)
            .Take(limit)
            .ToListAsync();

        return deployments.Select(d => new ApiDeploymentDto
        {
            Id = d.Id,
            ApplicationServerId = d.ApplicationServerId,
            ServerName = d.ApplicationServer.ServerName,
            ApplicationName = d.ApplicationServer.Application.Name,
            DeployedBy = d.DeployedByUser.FullName ?? d.DeployedByUser.Username,
            DeploymentDate = d.DeploymentDate,
            Version = d.Version,
            Status = d.Status,
            DeploymentType = d.DeploymentType.ToString(),
            BackupFileName = d.BackupFileName,
            ErrorMessage = d.ErrorMessage,
            DeploymentDurationSeconds = d.DeploymentDurationSeconds,
            StartedAt = d.StartedAt,
            CompletedAt = d.CompletedAt,
            Files = d.DeploymentFiles.Select(f => new ApiDeploymentFileDto
            {
                Id = f.Id,
                FileName = f.FileName,
                FileSize = f.FileSize,
                FileHash = f.FileHash,
                UploadedAt = f.UploadedAt
            }).ToList()
        }).ToList();
    }

    public async Task<ApiDeploymentDto?> GetDeploymentByIdAsync(int deploymentId)
    {
        var deployment = await _context.ApiDeployments
            .Include(d => d.ApplicationServer)
                .ThenInclude(s => s.Application)
            .Include(d => d.DeployedByUser)
            .Include(d => d.DeploymentFiles)
            .FirstOrDefaultAsync(d => d.Id == deploymentId);

        if (deployment == null) return null;

        return new ApiDeploymentDto
        {
            Id = deployment.Id,
            ApplicationServerId = deployment.ApplicationServerId,
            ServerName = deployment.ApplicationServer.ServerName,
            ApplicationName = deployment.ApplicationServer.Application.Name,
            DeployedBy = deployment.DeployedByUser.FullName ?? deployment.DeployedByUser.Username,
            DeploymentDate = deployment.DeploymentDate,
            Version = deployment.Version,
            Status = deployment.Status,
            DeploymentType = deployment.DeploymentType.ToString(),
            BackupFileName = deployment.BackupFileName,
            ErrorMessage = deployment.ErrorMessage,
            DeploymentDurationSeconds = deployment.DeploymentDurationSeconds,
            StartedAt = deployment.StartedAt,
            CompletedAt = deployment.CompletedAt,
            Files = deployment.DeploymentFiles.Select(f => new ApiDeploymentFileDto
            {
                Id = f.Id,
                FileName = f.FileName,
                FileSize = f.FileSize,
                FileHash = f.FileHash,
                UploadedAt = f.UploadedAt
            }).ToList()
        };
    }

    public async Task<(bool Success, string? FilePath, string Message)> DownloadBackupAsync(int deploymentId)
    {
        var deployment = await _context.ApiDeployments
            .Include(d => d.ApplicationServer)
            .FirstOrDefaultAsync(d => d.Id == deploymentId);

        if (deployment == null)
        {
            return (false, null, "Deployment not found");
        }

        if (string.IsNullOrEmpty(deployment.BackupFileName))
        {
            return (false, null, "No backup file associated with this deployment");
        }

        // Download backup from remote server
        var server = deployment.ApplicationServer;
        var remoteBackupPath = $"{server.BackupPath}\\{deployment.BackupFileName}".Replace("\\\\", "\\");
        
        var localBackupDir = Path.Combine(_configuration["FileUploadSettings:BackupPath"] ?? "Backups/ApiDeployments", deploymentId.ToString());
        Directory.CreateDirectory(localBackupDir);
        
        var localBackupPath = Path.Combine(localBackupDir, deployment.BackupFileName);

        var (password, privateKey) = await GetCredentialsAsync(server);
        
        var success = await _sshService.DownloadFileAsync(
            server.HostName, server.Port, server.Username, password, privateKey,
            remoteBackupPath, localBackupPath);

        if (success)
        {
            deployment.LocalBackupPath = localBackupPath;
            await _context.SaveChangesAsync();
            
            return (true, localBackupPath, "Backup downloaded successfully");
        }

        return (false, null, "Failed to download backup file");
    }

    public async Task<ApiDeploymentResultDto> DeployFrontendAsync(DeployFrontendRequest request, int userId)
    {
        var stopwatch = Stopwatch.StartNew();
        var deploymentSteps = new List<string>();
        var startTime = DateTime.UtcNow;

        var server = await _context.ApplicationServers
            .Include(s => s.Application)
            .FirstOrDefaultAsync(s => s.Id == request.ApplicationServerId);

        if (server == null)
        {
            return new ApiDeploymentResultDto
            {
                Status = ApiDeploymentStatus.Failed,
                Message = "Application server not found",
                ErrorMessage = "Server configuration does not exist",
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            };
        }

        var deployment = new ApiDeployment
        {
            ApplicationServerId = request.ApplicationServerId,
            DeployedBy = userId,
            Version = request.Version,
            Status = ApiDeploymentStatus.Pending,
            DeploymentType = AppDeploymentType.Frontend,
            StartedAt = startTime
        };

        _context.ApiDeployments.Add(deployment);
        await _context.SaveChangesAsync();

        // Save ZIP to temp
        var tempDir = Path.Combine(Path.GetTempPath(), "DeployVare", $"frontend_{deployment.Id}");
        Directory.CreateDirectory(tempDir);
        var localZipPath = Path.Combine(tempDir, "upload.zip");

        try
        {
            using (var stream = new FileStream(localZipPath, FileMode.Create))
            {
                await request.ZipFile.CopyToAsync(stream);
            }

            // Step 1: Stop services
            deploymentSteps.Add($"Stopping IIS services on {server.ServerName}...");
            deployment.Status = ApiDeploymentStatus.StoppingServices;
            await _context.SaveChangesAsync();

            var (appPoolStopped, appPoolMsg) = await _iisService.StopAppPoolAsync(server);
            if (!appPoolStopped) throw new Exception($"Failed to stop App Pool: {appPoolMsg}");
            deploymentSteps.Add($"✓ App Pool stopped");

            var (websiteStopped, websiteMsg) = await _iisService.StopWebsiteAsync(server);
            if (!websiteStopped) throw new Exception($"Failed to stop Website: {websiteMsg}");
            deploymentSteps.Add($"✓ Website stopped");

            // Step 2: Backup
            deploymentSteps.Add("Creating backup of existing files...");
            deployment.Status = ApiDeploymentStatus.CreatingBackup;
            await _context.SaveChangesAsync();

            var (backupCreated, backupFileName, backupMsg) = await _iisService.CreateBackupAsync(server);
            if (!backupCreated) throw new Exception($"Failed to create backup: {backupMsg}");

            if (!string.IsNullOrEmpty(backupFileName))
            {
                deployment.BackupFileName = backupFileName;
                await _context.SaveChangesAsync();
                deploymentSteps.Add($"✓ Backup created: {backupFileName}");
            }
            else
            {
                deploymentSteps.Add($"⊘ Backup skipped: {backupMsg}");
            }

            // Step 3: Upload and extract ZIP
            deploymentSteps.Add("Uploading and extracting frontend ZIP...");
            deployment.Status = ApiDeploymentStatus.UploadingFiles;
            await _context.SaveChangesAsync();

            var (deployed, deployMsg) = await _iisService.DeployFrontendZipAsync(server, localZipPath);
            if (!deployed) throw new Exception($"Failed to deploy frontend: {deployMsg}");
            deploymentSteps.Add($"✓ Frontend files extracted successfully");

            // Step 4: Start services
            deploymentSteps.Add("Starting IIS services...");
            deployment.Status = ApiDeploymentStatus.StartingServices;
            await _context.SaveChangesAsync();

            var (websiteStarted, websiteStartMsg) = await _iisService.StartWebsiteAsync(server);
            if (!websiteStarted) throw new Exception($"Failed to start Website: {websiteStartMsg}");
            deploymentSteps.Add($"✓ Website started");

            var (appPoolStarted, appPoolStartMsg) = await _iisService.StartAppPoolAsync(server);
            if (!appPoolStarted) throw new Exception($"Failed to start App Pool: {appPoolStartMsg}");
            deploymentSteps.Add($"✓ App Pool started");

            // Step 5: Verify
            deploymentSteps.Add("Verifying deployment...");
            deployment.Status = ApiDeploymentStatus.Verifying;
            await _context.SaveChangesAsync();

            await Task.Delay(2000);

            var (verified, verifyMsg) = await _iisService.VerifyDeploymentAsync(server);
            deploymentSteps.Add(verified
                ? "✓ Deployment verified successfully"
                : $"⚠ Verification warning: {verifyMsg}");

            stopwatch.Stop();
            deployment.Status = ApiDeploymentStatus.Success;
            deployment.CompletedAt = DateTime.UtcNow;
            deployment.DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
            await _context.SaveChangesAsync();

            try { Directory.Delete(tempDir, true); } catch { }

            deploymentSteps.Add($"✓ Deployment completed in {stopwatch.Elapsed.TotalSeconds:F1} seconds!");
            _logger.LogInformation($"Frontend deployment {deployment.Id} completed successfully");

            return new ApiDeploymentResultDto
            {
                DeploymentId = deployment.Id,
                Status = ApiDeploymentStatus.Success,
                Message = "Frontend deployment completed successfully",
                BackupFileName = backupFileName,
                DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds,
                DeploymentSteps = deploymentSteps,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, $"Frontend deployment {deployment.Id} failed");

            deployment.Status = ApiDeploymentStatus.Failed;
            deployment.ErrorMessage = ex.Message;
            deployment.CompletedAt = DateTime.UtcNow;
            deployment.DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
            await _context.SaveChangesAsync();

            deploymentSteps.Add($"✗ Deployment failed: {ex.Message}");

            try
            {
                await _iisService.StartWebsiteAsync(server);
                await _iisService.StartAppPoolAsync(server);
                deploymentSteps.Add("Services restarted after failure");
            }
            catch (Exception restartEx)
            {
                deploymentSteps.Add($"Failed to restart services: {restartEx.Message}");
            }

            try { Directory.Delete(tempDir, true); } catch { }

            return new ApiDeploymentResultDto
            {
                DeploymentId = deployment.Id,
                Status = ApiDeploymentStatus.Failed,
                Message = "Frontend deployment failed",
                ErrorMessage = ex.Message,
                DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds,
                DeploymentSteps = deploymentSteps,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            };
        }
    }

    private async Task<Dictionary<string, (long size, string hash)>> SaveUploadedFilesAsync(List<IFormFile> files, int deploymentId)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "DeployVare", deploymentId.ToString());
        Directory.CreateDirectory(tempDir);

        var fileMappings = new Dictionary<string, (long size, string hash)>();

        foreach (var file in files)
        {
            var filePath = Path.Combine(tempDir, file.FileName);
            
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Calculate file hash
            var hash = await CalculateFileHashAsync(filePath);
            var size = new FileInfo(filePath).Length;

            // Save to database
            var deploymentFile = new ApiDeploymentFile
            {
                ApiDeploymentId = deploymentId,
                FileName = file.FileName,
                FileSize = size,
                FileHash = hash
            };

            _context.ApiDeploymentFiles.Add(deploymentFile);
            fileMappings[filePath] = (size, hash);
        }

        await _context.SaveChangesAsync();
        return fileMappings;
    }

    private async Task<bool> UploadFilesToServerAsync(ApplicationServer server, Dictionary<string, (long size, string hash)> tempFiles)
    {
        var (password, privateKey) = await GetCredentialsAsync(server);

        // Create file mappings: local temp path -> remote deployment path
        var fileMappings = new Dictionary<string, string>();
        foreach (var (localPath, _) in tempFiles)
        {
            var fileName = Path.GetFileName(localPath);
            // Convert Windows path to Unix-style path for SCP (even on Windows servers)
            var remotePath = $"{server.DeploymentPath}/{fileName}".Replace("\\", "/");
            fileMappings[localPath] = remotePath;
        }

        return await _sshService.UploadFilesAsync(
            server.HostName, server.Port, server.Username, password, privateKey,
            fileMappings);
    }

    private void CleanupTempFiles(Dictionary<string, (long size, string hash)> tempFiles)
    {
        foreach (var (filePath, _) in tempFiles)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Failed to delete temp file: {filePath}");
            }
        }

        // Try to delete temp directory
        try
        {
            var tempDir = Path.GetDirectoryName(tempFiles.Keys.FirstOrDefault());
            if (!string.IsNullOrEmpty(tempDir) && Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete temp directory");
        }
    }

    private async Task<string> CalculateFileHashAsync(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private async Task<(string? password, string? privateKey)> GetCredentialsAsync(ApplicationServer server)
    {
        string? password = null;
        string? privateKey = null;

        if (!string.IsNullOrEmpty(server.EncryptedPassword))
        {
            password = await Task.Run(() => _encryptionService.Decrypt(server.EncryptedPassword));
        }

        if (!string.IsNullOrEmpty(server.SshPrivateKey))
        {
            privateKey = server.SshPrivateKey;
        }

        return (password, privateKey);
    }
}


