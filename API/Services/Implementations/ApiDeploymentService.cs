using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using API.Utilities;
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
    private readonly IAzureDevOpsService _azureDevOpsService;
    private readonly IAzureDevOpsConfigService _azureDevOpsConfigService;
    private readonly IServerReadinessService _serverReadinessService;
    private readonly IFrontendRuntimeConfigService _frontendRuntimeConfigService;
    private readonly IDatabaseMigrationService _databaseMigrationService;
    private readonly ILicensingService _licensingService;
    private readonly ILogger<ApiDeploymentService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IDeploymentProgressTracker _progress;

    public ApiDeploymentService(
        DeployVareDbContext context,
        ISshService sshService,
        IIisManagementService iisService,
        IEncryptionService encryptionService,
        IAzureDevOpsService azureDevOpsService,
        IAzureDevOpsConfigService azureDevOpsConfigService,
        IServerReadinessService serverReadinessService,
        IFrontendRuntimeConfigService frontendRuntimeConfigService,
        IDatabaseMigrationService databaseMigrationService,
        ILicensingService licensingService,
        ILogger<ApiDeploymentService> logger,
        IConfiguration configuration,
        IDeploymentProgressTracker progressTracker)
    {
        _progress = progressTracker;
        _context = context;
        _sshService = sshService;
        _iisService = iisService;
        _encryptionService = encryptionService;
        _azureDevOpsService = azureDevOpsService;
        _azureDevOpsConfigService = azureDevOpsConfigService;
        _serverReadinessService = serverReadinessService;
        _frontendRuntimeConfigService = frontendRuntimeConfigService;
        _databaseMigrationService = databaseMigrationService;
        _licensingService = licensingService;
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

        // Create deployment record. Uploading files by hand carries no licence, so this is recorded as an
        // unlicensed deployment rather than left to the default.
        var deployment = new ApiDeployment
        {
            ApplicationServerId = request.ApplicationServerId,
            DeployedBy = userId,
            Version = request.Version,
            Status = ApiDeploymentStatus.Pending,
            LicensingMode = LicensingMode.WithoutLicense,
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
            LicensingMode = d.LicensingMode.ToString(),
            LicenseId = d.LicenseId,
            ArtifactEnvironment = d.ArtifactEnvironment,
            AzureBuildId = d.AzureBuildId,
            AzureBuildNumber = d.AzureBuildNumber,
            AzureArtifactName = d.AzureArtifactName,
            SourceBranch = d.SourceBranch,
            SourceVersion = d.SourceVersion,
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
            LicensingMode = deployment.LicensingMode.ToString(),
            LicenseId = deployment.LicenseId,
            ArtifactEnvironment = deployment.ArtifactEnvironment,
            AzureBuildId = deployment.AzureBuildId,
            AzureBuildNumber = deployment.AzureBuildNumber,
            AzureArtifactName = deployment.AzureArtifactName,
            SourceBranch = deployment.SourceBranch,
            SourceVersion = deployment.SourceVersion,
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
        
        var localBackupBase = _configuration["FileUploadSettings:BackupPath"] ?? "Backups/ApiDeployments";
        var localBackupDir = Path.IsPathRooted(localBackupBase)
            ? Path.Combine(localBackupBase, "ApiDeployments", deploymentId.ToString())
            : Path.Combine(Directory.GetCurrentDirectory(), localBackupBase, deploymentId.ToString());
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
            LicensingMode = LicensingMode.WithoutLicense,
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

            // Replace the build's config.js with this server's runtime settings
            await WriteRuntimeConfigStepAsync(server, deploymentSteps);

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

    public async Task<ApiDeploymentResultDto> DeployFromAzureArtifactAsync(DeployAzureArtifactRequest request, int userId,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var deploymentSteps = new List<string>();
        var startTime = DateTime.UtcNow;

        ArtifactDeploymentContext artifactContext;
        try
        {
            artifactContext = await _azureDevOpsConfigService.ResolveDeploymentContextAsync(
                request.ApplicationServerId, request.Environment, cancellationToken);
        }
        catch (AzureDevOpsException ex)
        {
            return new ApiDeploymentResultDto
            {
                Status = ApiDeploymentStatus.Failed,
                Message = "Deployment could not be started",
                ErrorMessage = ex.Message,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            };
        }

        var server = artifactContext.Server;
        var source = artifactContext.Source;

        // Resolve the licence before anything is uploaded. A licensed deployment that discovered a problem after
        // IIS had been stopped would leave the site down for a reason the operator could have been told up front.
        License? licenseToInstall;
        try
        {
            licenseToInstall = await ResolveLicenseAsync(request, server, cancellationToken);
        }
        catch (LicensingException ex)
        {
            return new ApiDeploymentResultDto
            {
                Status = ApiDeploymentStatus.Failed,
                Message = "Deployment could not be started",
                ErrorMessage = ex.Message,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            };
        }

        var deployment = new ApiDeployment
        {
            ApplicationServerId = server.Id,
            DeployedBy = userId,
            Version = request.Version,
            Status = ApiDeploymentStatus.Pending,
            DeploymentType = server.ServerType == ServerType.Frontend ? AppDeploymentType.Frontend : AppDeploymentType.Api,
            ArtifactEnvironment = request.Environment.ToString(),
            AzureBuildId = request.BuildId,
            AzureArtifactName = Truncate(request.ArtifactName, 200),
            LicensingMode = request.LicensingMode,
            LicenseId = licenseToInstall?.LicenseId,
            StartedAt = startTime
        };

        _context.ApiDeployments.Add(deployment);
        await _context.SaveChangesAsync();

        // Live progress for the Deploy App page (no-op when the request has no progress ID)
        void Begin(string stage) => _progress.Begin(request.ProgressId, stage);
        void Done(string stage, DeploymentStageState state = DeploymentStageState.Succeeded, string? detail = null) =>
            _progress.Complete(request.ProgressId, stage, state, detail);

        _progress.Start(request.ProgressId, deployment.Id, DeploymentStages.AzureArtifact);

        // Migrations need an API server, the source setting and a linked database instance. A server without a
        // database is fine: the deployment then runs without touching any database.
        var migrationsEnabled = server.ServerType == ServerType.Api
            && source.RunDatabaseMigrations
            && server.DatabaseInstanceId != null;

        if (!migrationsEnabled)
        {
            var reason = server.ServerType == ServerType.Frontend
                ? "Not used for frontend servers"
                : !source.RunDatabaseMigrations
                    ? "Database migrations are not enabled for this source"
                    : "No database instance is linked to this server";
            Done(DeploymentStages.DatabaseCheck, DeploymentStageState.Skipped, reason);
            Done(DeploymentStages.Migrations, DeploymentStageState.Skipped, reason);
        }

        var workspace = Path.Combine(Path.GetTempPath(), "DeployVare", "artifacts", $"{deployment.Id}_{Guid.NewGuid():N}");
        var servicesStopped = false;
        string? backupFileName = null;
        string? remoteWorkspace = null;

        _logger.LogInformation("DeploymentStarted {DeploymentId}: build {BuildId} artifact {ArtifactName} ({Environment}) to {ServerName} ({Host})",
            deployment.Id, request.BuildId, request.ArtifactName, request.Environment, server.ServerName, server.HostName);

        try
        {
            // Step 1: Azure DevOps connection
            Begin(DeploymentStages.AzureDevOps);
            deploymentSteps.Add("Connecting to Azure DevOps...");
            await _azureDevOpsService.TestConnectionAsync(artifactContext.Credentials, cancellationToken);
            deploymentSteps.Add("✓ Azure DevOps connection successful");
            Done(DeploymentStages.AzureDevOps);

            // Step 2: Client server connection (existing SSH mechanism)
            Begin(DeploymentStages.Server);
            deploymentSteps.Add($"Connecting to client server {server.ServerName}...");
            var (password, privateKey) = await GetCredentialsAsync(server);
            if (!await _sshService.TestConnectionAsync(server.HostName, server.Port, server.Username, password, privateKey))
            {
                throw new Exception($"Could not connect to server {server.ServerName} ({server.HostName}:{server.Port})");
            }
            deploymentSteps.Add("✓ Server connection successful");

            // Step 2b: Server readiness (IIS, site, runtimes, Redis, SSL, bindings)
            deploymentSteps.Add("Checking server readiness...");
            var readiness = await _serverReadinessService.CheckAsync(server.Id, $"deployment #{deployment.Id}");
            if (!readiness.IsReady)
            {
                var failing = readiness.Items
                    .Where(i => i.Required && i.Status != DTOs.Responses.ReadinessStatus.Pass)
                    .Select(i => i.Name);
                throw new Exception($"Server is not ready for deployment: {string.Join(", ", failing)}. Complete Server Setup first.");
            }
            deploymentSteps.Add("✓ Server readiness verified");
            Done(DeploymentStages.Server);

            // Step 3: Re-validate the requested build against the configured source
            Begin(DeploymentStages.Build);
            deploymentSteps.Add($"Verifying build {request.BuildId}...");
            var build = await _azureDevOpsService.GetEligibleBuildAsync(artifactContext.Credentials, request.BuildId,
                source.Repository, source.Branch, source.WindowsPipelineId, cancellationToken);

            if (!string.IsNullOrWhiteSpace(source.ArtifactName)
                && !source.ArtifactName.Equals(request.ArtifactName, StringComparison.OrdinalIgnoreCase))
            {
                throw new AzureDevOpsException(AzureDevOpsErrorCode.BuildNotEligible,
                    $"Only the configured artifact '{source.ArtifactName}' can be deployed from this source.");
            }

            var artifact = build.Artifacts.FirstOrDefault(a => a.Name.Equals(request.ArtifactName, StringComparison.OrdinalIgnoreCase))
                ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotFound,
                    $"Build #{build.BuildNumber} has no artifact named '{request.ArtifactName}'.");

            if (!artifact.IsDownloadable)
            {
                throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotDownloadable,
                    $"Artifact '{artifact.Name}' (type '{artifact.ResourceType}') cannot be downloaded.");
            }

            deployment.AzureBuildNumber = Truncate(build.BuildNumber, 100);
            deployment.AzureArtifactName = Truncate(artifact.Name, 200);
            deployment.SourceBranch = Truncate(build.SourceBranch, 250);
            deployment.SourceVersion = Truncate(build.SourceVersion, 64);
            deployment.Version ??= Truncate(build.BuildNumber, 50);
            await _context.SaveChangesAsync();

            var commit = build.SourceVersion.Length > 8 ? build.SourceVersion[..8] : build.SourceVersion;
            deploymentSteps.Add($"✓ Build #{build.BuildNumber} verified ({source.Repository}@{source.Branch}, commit {commit})");
            Done(DeploymentStages.Build);

            // Step 4: Download artifact into a temporary workspace
            Begin(DeploymentStages.Download);
            deploymentSteps.Add($"Downloading artifact '{artifact.Name}'...");
            var artifactZipPath = Path.Combine(workspace, "artifact.zip");
            await _azureDevOpsService.DownloadArtifactAsync(artifactContext.Credentials, build.Id, artifact.Name, artifactZipPath, cancellationToken);
            deploymentSteps.Add($"✓ Artifact downloaded ({new FileInfo(artifactZipPath).Length / 1024d / 1024d:F1} MB)");
            Done(DeploymentStages.Download);

            // Step 5: Extract and validate the deployment package
            Begin(DeploymentStages.Package);
            deploymentSteps.Add("Extracting and validating deployment package...");
            var package = await Task.Run(() => ArtifactPackageBuilder.Build(
                artifactZipPath, workspace, artifact.Name, server.ServerType, source.ArtifactSubfolder), cancellationToken);

            foreach (var warning in package.Warnings)
            {
                deploymentSteps.Add($"⚠ {warning}");
            }

            foreach (var file in package.Files)
            {
                _context.ApiDeploymentFiles.Add(new ApiDeploymentFile
                {
                    ApiDeploymentId = deployment.Id,
                    FileName = file.RelativePath.Length > 200 ? "…" + file.RelativePath[^199..] : file.RelativePath,
                    FileSize = file.Size,
                    FileHash = file.Sha256
                });
            }
            await _context.SaveChangesAsync();
            deploymentSteps.Add($"✓ Package extracted and validated: {package.Files.Count} files"
                + (string.IsNullOrWhiteSpace(source.ArtifactSubfolder) ? string.Empty : $" from '{source.ArtifactSubfolder}'"));
            Done(DeploymentStages.Package,
                package.Warnings.Count > 0 ? DeploymentStageState.Warning : DeploymentStageState.Succeeded,
                package.Warnings.Count > 0 ? string.Join(" ", package.Warnings) : null);

            // Step 5b: Backend sources with migrations also need this build's SQL scripts artifact
            var runMigrations = migrationsEnabled;
            if (server.ServerType == ServerType.Api && source.RunDatabaseMigrations && server.DatabaseInstanceId == null)
            {
                deploymentSteps.Add("⊘ Database migrations skipped: no database instance is linked to this server (Server Setup > Database Access)");
            }
            string? scriptsPackageZip = null;
            string? migrationDataSource = null;
            string? migrationDatabase = null;

            if (runMigrations)
            {
                Begin(DeploymentStages.DatabaseCheck);
                deploymentSteps.Add("Preparing database migrations...");

                if (!package.Files.Any(f => Path.GetFileName(f.RelativePath).Equals("DbMigrator.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new Exception("DbMigrator.exe was not found in the deployment package, so database migrations cannot run.");
                }

                (migrationDataSource, migrationDatabase) = await _databaseMigrationService.ResolveTargetAsync(server);

                var scriptsArtifactName = string.IsNullOrWhiteSpace(source.SqlScriptsArtifactName) ? "sqlscripts" : source.SqlScriptsArtifactName;
                var scriptsArtifact = build.Artifacts.FirstOrDefault(a =>
                        a.IsDownloadable && a.Name.Equals(scriptsArtifactName, StringComparison.OrdinalIgnoreCase))
                    ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotFound,
                        $"Build #{build.BuildNumber} has no downloadable '{scriptsArtifactName}' artifact with the SQL scripts.");

                // Safety check before DbMigrator ever connects: migrate only an existing, DbUp-managed application
                // database. A missing or empty database (e.g. a fresh client server) skips migrations; server, access
                // and schema problems stop the deployment before anything on the server changes.
                deploymentSteps.Add($"Checking database '{migrationDatabase}' on {migrationDataSource} before migrating...");
                var target = await _databaseMigrationService.CheckTargetAsync(server, migrationDataSource!, migrationDatabase!);

                switch (target.Outcome)
                {
                    case MigrationTargetOutcome.Skip:
                        deploymentSteps.Add($"⊘ Database migrations skipped: {target.Message}");
                        runMigrations = false;
                        Done(DeploymentStages.DatabaseCheck, detail: target.Message);
                        Done(DeploymentStages.Migrations, DeploymentStageState.Skipped, target.Message);
                        break;

                    case MigrationTargetOutcome.Block:
                        throw new Exception($"Database check failed before migrating: {target.Message} The site was not changed.");

                    default:
                        deploymentSteps.Add($"✓ {target.Message}");
                        break;
                }

                if (runMigrations)
                {
                    var scriptsArtifactZip = Path.Combine(workspace, "sqlscripts-artifact.zip");
                    await _azureDevOpsService.DownloadArtifactAsync(artifactContext.Credentials, build.Id, scriptsArtifact.Name, scriptsArtifactZip, cancellationToken);

                    var scripts = await Task.Run(() =>
                        ArtifactPackageBuilder.BuildScriptsPackage(scriptsArtifactZip, workspace, scriptsArtifact.Name), cancellationToken);
                    scriptsPackageZip = scripts.PackageZipPath;

                    deploymentSteps.Add($"✓ SQL scripts ready: {scripts.SqlFileCount} file(s) from '{scriptsArtifact.Name}' for database '{migrationDatabase}' on {migrationDataSource}");
                    Done(DeploymentStages.DatabaseCheck);
                }
            }

            // Step 5c: Upload the package while the site keeps running (shorter downtime)
            Begin(DeploymentStages.Upload);
            deploymentSteps.Add($"Uploading package to {server.ServerName}...");
            deployment.Status = ApiDeploymentStatus.UploadingFiles;
            await _context.SaveChangesAsync();

            var (workspaceCreated, workspaceMessage, createdWorkspace) = await _iisService.CreateRemoteWorkspaceAsync(server);
            if (!workspaceCreated) throw new Exception(workspaceMessage);
            remoteWorkspace = createdWorkspace;

            var (packageStaged, packageStageMessage, stagedPackagePath) =
                await _iisService.UploadAndExtractZipAsync(server, remoteWorkspace!, package.PackageZipPath, "package");
            if (!packageStaged) throw new Exception($"Failed to upload package: {packageStageMessage}");
            deploymentSteps.Add($"✓ Package uploaded ({new FileInfo(package.PackageZipPath).Length / 1024d / 1024d:F1} MB)");
            Done(DeploymentStages.Upload);

            // Step 5d: Database migrations run before IIS is touched, like the pipeline; a failure leaves the site as it was
            if (runMigrations)
            {
                Begin(DeploymentStages.Migrations);
                var (scriptsStaged, scriptsStageMessage, stagedScriptsPath) =
                    await _iisService.UploadAndExtractZipAsync(server, remoteWorkspace!, scriptsPackageZip!, "sqlscripts");
                if (!scriptsStaged) throw new Exception($"Failed to upload SQL scripts: {scriptsStageMessage}");

                deploymentSteps.Add($"Running DbMigrator on database '{migrationDatabase}'...");
                deployment.Status = ApiDeploymentStatus.MigratingDatabase;
                await _context.SaveChangesAsync();

                var migration = await _databaseMigrationService.RunDbMigratorAsync(
                    server, migrationDataSource!, migrationDatabase!, stagedPackagePath!, stagedScriptsPath!);

                if (!migration.Success)
                {
                    deploymentSteps.AddRange(migration.OutputTail.Select(line => $"    {line}"));
                    throw new Exception(
                        $"Database migration failed: {migration.Message}. The site was not changed; scripts that completed before the failure remain applied.");
                }

                deploymentSteps.Add($"✓ {migration.Message}");
                Done(DeploymentStages.Migrations, detail: migration.Message);
            }

            // From here on the site is changed; cancellation is no longer honoured
            // so a disconnected browser cannot leave the site stopped halfway through.

            // Step 6: Stop IIS services
            Begin(DeploymentStages.StopServices);
            deploymentSteps.Add($"Stopping IIS services on {server.ServerName}...");
            deployment.Status = ApiDeploymentStatus.StoppingServices;
            await _context.SaveChangesAsync();
            servicesStopped = true;

            var (appPoolStopped, appPoolMsg) = await _iisService.StopAppPoolAsync(server);
            if (!appPoolStopped) throw new Exception($"Failed to stop App Pool: {appPoolMsg}");
            deploymentSteps.Add("✓ App Pool stopped");

            var (websiteStopped, websiteMsg) = await _iisService.StopWebsiteAsync(server);
            if (!websiteStopped) throw new Exception($"Failed to stop Website: {websiteMsg}");
            deploymentSteps.Add("✓ Website stopped");
            Done(DeploymentStages.StopServices);

            // Step 7: Backup
            Begin(DeploymentStages.Backup);
            deploymentSteps.Add("Creating backup of existing files...");
            deployment.Status = ApiDeploymentStatus.CreatingBackup;
            await _context.SaveChangesAsync();

            var (backupCreated, createdBackupFileName, backupMsg) = await _iisService.CreateBackupAsync(server);
            if (!backupCreated) throw new Exception($"Failed to create backup: {backupMsg}");

            if (!string.IsNullOrEmpty(createdBackupFileName))
            {
                backupFileName = createdBackupFileName;
                deployment.BackupFileName = backupFileName;
                await _context.SaveChangesAsync();
                deploymentSteps.Add($"✓ Backup created: {backupFileName}");
            }
            else
            {
                deploymentSteps.Add($"⊘ Backup skipped: {backupMsg}");
            }

            if (string.IsNullOrEmpty(createdBackupFileName))
                Done(DeploymentStages.Backup, DeploymentStageState.Skipped, backupMsg);
            else
                Done(DeploymentStages.Backup);

            // Step 8: Deploy files
            Begin(DeploymentStages.DeployFiles);
            deploymentSteps.Add("Deploying files...");
            deployment.Status = ApiDeploymentStatus.UploadingFiles;
            await _context.SaveChangesAsync();

            var (deployed, deployMsg) = await _iisService.PublishStagedFolderAsync(
                server, stagedPackagePath!, source.PreserveWebConfig, KeepOnServerPaths.Parse(source.KeepOnServerPaths));
            if (!deployed) throw new Exception($"Failed to deploy package: {deployMsg}");
            deploymentSteps.Add($"✓ {deployMsg}");

            // Frontend servers: replace the build's config.js with this server's runtime settings
            if (server.ServerType == ServerType.Frontend)
            {
                await WriteRuntimeConfigStepAsync(server, deploymentSteps);
            }

            Done(DeploymentStages.DeployFiles);

            // Step 8b: Licence, while IIS is still stopped. Skipped entirely for an ordinary deployment, which
            // is what keeps existing clients and the Dev server behaving exactly as before.
            if (licenseToInstall == null)
            {
                const string reason = "Deploying without a licence";
                Done(DeploymentStages.InstallLicense, DeploymentStageState.Skipped, reason);
                Done(DeploymentStages.VerifyLicense, DeploymentStageState.Skipped, reason);
            }
            else
            {
                Begin(DeploymentStages.InstallLicense);
                await InstallLicenseStepAsync(server, licenseToInstall, deploymentSteps);
                Done(DeploymentStages.InstallLicense);

                Begin(DeploymentStages.VerifyLicense);
                await VerifyLicenseStepAsync(server, licenseToInstall, deploymentSteps);
                Done(DeploymentStages.VerifyLicense);
            }

            // Step 9: Start IIS services
            Begin(DeploymentStages.StartServices);
            deploymentSteps.Add("Starting IIS services...");
            deployment.Status = ApiDeploymentStatus.StartingServices;
            await _context.SaveChangesAsync();

            var (websiteStarted, websiteStartMsg) = await _iisService.StartWebsiteAsync(server);
            if (!websiteStarted) throw new Exception($"Failed to start Website: {websiteStartMsg}");
            deploymentSteps.Add("✓ Website started");

            var (appPoolStarted, appPoolStartMsg) = await _iisService.StartAppPoolAsync(server);
            if (!appPoolStarted) throw new Exception($"Failed to start App Pool: {appPoolStartMsg}");
            deploymentSteps.Add("✓ App Pool started");
            servicesStopped = false;

            Done(DeploymentStages.StartServices);

            // Step 10: Verify
            Begin(DeploymentStages.Verify);
            deploymentSteps.Add("Verifying deployment...");
            deployment.Status = ApiDeploymentStatus.Verifying;
            await _context.SaveChangesAsync();

            await Task.Delay(2000); // Wait for services to stabilize

            var (verified, verifyMsg) = await _iisService.VerifyDeploymentAsync(server);
            if (verified)
            {
                _logger.LogInformation("DeploymentVerificationSucceeded {DeploymentId}", deployment.Id);
                deploymentSteps.Add("✓ Deployment verified successfully");
            }
            else
            {
                _logger.LogWarning("DeploymentVerificationFailed {DeploymentId}: {Message}", deployment.Id, verifyMsg);
                deploymentSteps.Add($"⚠ Verification warning: {verifyMsg}");
            }

            stopwatch.Stop();
            deployment.Status = ApiDeploymentStatus.Success;
            deployment.CompletedAt = DateTime.UtcNow;
            deployment.DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
            await _context.SaveChangesAsync();

            Done(DeploymentStages.Verify,
                verified ? DeploymentStageState.Succeeded : DeploymentStageState.Warning,
                verified ? null : verifyMsg);
            _progress.Finish(request.ProgressId, succeeded: true);

            deploymentSteps.Add($"✓ Deployment completed successfully in {stopwatch.Elapsed.TotalSeconds:F1} seconds!");
            _logger.LogInformation("DeploymentSucceeded {DeploymentId} build #{BuildNumber} in {Seconds}s",
                deployment.Id, build.BuildNumber, (int)stopwatch.Elapsed.TotalSeconds);

            var savedFiles = await _context.ApiDeploymentFiles
                .Where(f => f.ApiDeploymentId == deployment.Id)
                .ToListAsync();

            return new ApiDeploymentResultDto
            {
                DeploymentId = deployment.Id,
                Status = ApiDeploymentStatus.Success,
                Message = $"Build #{build.BuildNumber} deployed successfully",
                BackupFileName = backupFileName,
                DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds,
                DeploymentSteps = deploymentSteps,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow,
                DeployedFiles = savedFiles.Select(f => new ApiDeploymentFileDto
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

            var errorMessage = ex is OperationCanceledException && !servicesStopped
                ? "Deployment was cancelled before any changes were made to the server."
                : ex.Message;

            if (ex is AzureDevOpsException azureEx)
                _logger.LogWarning("DeploymentFailed {DeploymentId}: {ErrorCode} {Message}", deployment.Id, azureEx.ErrorCode, azureEx.Message);
            else
                _logger.LogError(ex, "DeploymentFailed {DeploymentId}", deployment.Id);

            deployment.Status = ApiDeploymentStatus.Failed;
            deployment.ErrorMessage = errorMessage;
            deployment.CompletedAt = DateTime.UtcNow;
            deployment.DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
            await _context.SaveChangesAsync(CancellationToken.None);

            deploymentSteps.Add($"✗ Deployment failed: {errorMessage}");
            _progress.Fail(request.ProgressId, errorMessage);
            _progress.Finish(request.ProgressId, succeeded: false);

            // Only restart services this deployment stopped
            if (servicesStopped)
            {
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
            }

            return new ApiDeploymentResultDto
            {
                DeploymentId = deployment.Id,
                Status = ApiDeploymentStatus.Failed,
                Message = "Deployment failed",
                ErrorMessage = errorMessage,
                BackupFileName = backupFileName,
                DeploymentDurationSeconds = (int)stopwatch.Elapsed.TotalSeconds,
                DeploymentSteps = deploymentSteps,
                StartedAt = startTime,
                CompletedAt = DateTime.UtcNow
            };
        }
        finally
        {
            if (remoteWorkspace != null)
            {
                await _iisService.RemoveRemoteWorkspaceAsync(server, remoteWorkspace);
            }

            try
            {
                if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to delete artifact workspace {Workspace}", workspace);
            }
        }
    }

    /// <summary>
    /// Finds the licence a "With License" deployment should install, and refuses anything that would bind the
    /// wrong licence to this server. Returns null for an ordinary unlicensed deployment.
    ///
    /// Note that the real guarantee that one licence reaches only one server is the filtered unique index
    /// UQ_License_ActiveActivation in the database. The checks here exist to fail early with a clear message.
    /// </summary>
    private async Task<License?> ResolveLicenseAsync(
        DeployAzureArtifactRequest request, ApplicationServer server, CancellationToken cancellationToken)
    {
        if (request.LicensingMode != LicensingMode.WithLicense) return null;

        if (server.ServerType == ServerType.Frontend)
            throw new LicensingException("A frontend deployment cannot install a licence; licence the API server instead.");

        if (request.LicenseId == null)
            throw new LicensingException("Select the licence to install, or deploy without a licence.");

        var license = await _context.Licenses
            .Include(l => l.Client)
            .Include(l => l.Activations)
            .FirstOrDefaultAsync(l => l.Id == request.LicenseId.Value, cancellationToken)
            ?? throw new LicensingException($"Licence {request.LicenseId} was not found.");

        // The licence belongs to a client; this server belongs to an application, which belongs to a client
        var serverClientId = await _context.Applications
            .Where(a => a.Id == server.ApplicationId)
            .Select(a => (int?)a.ClientId)
            .FirstOrDefaultAsync(cancellationToken);

        if (serverClientId == null)
            throw new LicensingException("This server's application has no client, so no licence can be matched to it.");

        if (license.ClientId != serverClientId.Value)
            throw new LicensingException(
                $"Licence {license.LicenseId} belongs to a different client than this server, so it cannot be installed here.");

        if (license.Status == LicenseStatus.Revoked)
            throw new LicensingException($"Licence {license.LicenseId} has been revoked.");

        if (license.ExpiresAt <= DateTime.UtcNow)
            throw new LicensingException(
                $"Licence {license.LicenseId} expired on {license.ExpiresAt:yyyy-MM-dd}. Renew it before deploying.");

        return license;
    }

    /// <summary>
    /// Installs the licence and confirms the activation, both before IIS starts. A licensed application refuses
    /// to start without a valid activation, so starting the site first would only produce an outage.
    /// </summary>
    private async Task InstallLicenseStepAsync(ApplicationServer server, License license, List<string> deploymentSteps)
    {
        deploymentSteps.Add($"Installing licence {license.LicenseId}...");

        var file = await _licensingService.GetLicenseFileAsync(license.Id);
        var localPath = Path.Combine(Path.GetTempPath(), "DeployVare", "licences", $"{Guid.NewGuid():N}.lic");
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);

        try
        {
            await File.WriteAllTextAsync(localPath, file.Content);

            var (installed, installMessage) = await _iisService.InstallLicenseAsync(server, localPath);
            if (!installed) throw new Exception(installMessage);

            deploymentSteps.Add($"✓ {installMessage}");
        }
        finally
        {
            try { if (File.Exists(localPath)) File.Delete(localPath); }
            catch (IOException ex) { _logger.LogWarning(ex, "Could not delete the temporary licence file"); }
        }
    }

    /// <summary>
    /// Confirms the server accepts its licence. A server that has never been activated reports NotActivated,
    /// which is expected on a first licensed deployment and needs the activation exchange to finish.
    /// </summary>
    private async Task VerifyLicenseStepAsync(ApplicationServer server, License license, List<string> deploymentSteps)
    {
        var (valid, state, detail) = await _iisService.CheckLicenseStatusAsync(server);

        if (valid)
        {
            deploymentSteps.Add($"✓ Licence {license.LicenseId} is active on this server");
            return;
        }

        throw new Exception(
            $"The server does not accept licence {license.LicenseId} yet (state: {state}). {detail} " +
            "IIS has been left stopped, because a licensed application refuses to start without a valid activation. " +
            "Produce an activation request on the server, approve it in Licensing, apply the response, then start the site.");
    }

    private async Task WriteRuntimeConfigStepAsync(ApplicationServer server, List<string> deploymentSteps)
    {
        deploymentSteps.Add("Writing frontend runtime settings (config.js)...");
        var result = await _frontendRuntimeConfigService.WriteToServerAsync(server);

        if (result == null)
        {
            deploymentSteps.Add("⊘ No runtime settings configured for this server; the build's config.js (build-time values) is used");
            return;
        }

        if (!result.Value.Success)
        {
            throw new Exception(result.Value.Message);
        }

        deploymentSteps.Add($"✓ {result.Value.Message}");
    }

    private static string? Truncate(string? value, int maxLength) =>
        value == null || value.Length <= maxLength ? value : value[..maxLength];

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


