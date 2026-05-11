using API.Services.Interfaces;
using System.IO.Compression;

namespace API.Services.Implementations;

public class BackupService : IBackupService
{
    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly ILogger<BackupService> _logger;
    private readonly string _backupRootPath;
    private readonly IFileStorageService _storage;

    public BackupService(
        ISqlExecutionService sqlExecutionService,
        ILogger<BackupService> logger,
        IConfiguration configuration,
        IFileStorageService storage)
    {
        _sqlExecutionService = sqlExecutionService;
        _logger = logger;
        _storage = storage;
        
        var backupPath = configuration["FileUploadSettings:BackupPath"] ?? "wwwroot/backups";
        _backupRootPath = Path.Combine(Directory.GetCurrentDirectory(), backupPath);
        
        // Ensure backup directory exists
        Directory.CreateDirectory(_backupRootPath);
    }

    public async Task<string> BackupDatabaseObjectsAsync(
        string connectionString, 
        List<string> procedureNames, 
        List<string> functionNames, 
        int deploymentId)
    {
        try
        {
            _logger.LogInformation("Starting backup for deployment {DeploymentId}", deploymentId);

            // Create temporary folder for this backup
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var tempBackupFolder = Path.Combine(Path.GetTempPath(), $"backup_{deploymentId}_{timestamp}");
            Directory.CreateDirectory(tempBackupFolder);

            var backedUpCount = 0;

            // Backup stored procedures
            if (procedureNames.Any())
            {
                var proceduresFolder = Path.Combine(tempBackupFolder, "StoredProcedures");
                Directory.CreateDirectory(proceduresFolder);

                foreach (var procedureName in procedureNames)
                {
                    try
                    {
                        var definition = await _sqlExecutionService.GetStoredProcedureDefinitionAsync(connectionString, procedureName);
                        
                        if (!string.IsNullOrWhiteSpace(definition))
                        {
                            var fileName = $"{SanitizeFileName(procedureName)}.sql";
                            var filePath = Path.Combine(proceduresFolder, fileName);
                            await File.WriteAllTextAsync(filePath, definition);
                            backedUpCount++;
                            _logger.LogInformation("Backed up procedure: {ProcedureName}", procedureName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to backup procedure: {ProcedureName}", procedureName);
                    }
                }
            }

            // Backup functions
            if (functionNames.Any())
            {
                var functionsFolder = Path.Combine(tempBackupFolder, "Functions");
                Directory.CreateDirectory(functionsFolder);

                foreach (var functionName in functionNames)
                {
                    try
                    {
                        var definition = await _sqlExecutionService.GetFunctionDefinitionAsync(connectionString, functionName);
                        
                        if (!string.IsNullOrWhiteSpace(definition))
                        {
                            var fileName = $"{SanitizeFileName(functionName)}.sql";
                            var filePath = Path.Combine(functionsFolder, fileName);
                            await File.WriteAllTextAsync(filePath, definition);
                            backedUpCount++;
                            _logger.LogInformation("Backed up function: {FunctionName}", functionName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to backup function: {FunctionName}", functionName);
                    }
                }
            }

            // Create README with backup info
            var readmePath = Path.Combine(tempBackupFolder, "README.txt");
            var readmeContent = $@"DeployVare Database Objects Backup
Deployment ID: {deploymentId}
Backup Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}
Total Objects Backed Up: {backedUpCount}
Stored Procedures: {procedureNames.Count}
Functions: {functionNames.Count}

This backup was created automatically before deployment.
To restore, execute the SQL files in your database.
";
            await File.WriteAllTextAsync(readmePath, readmeContent);

            // Compress the backup folder
            var zipFileName = $"Backup_Deployment_{deploymentId}_{timestamp}.zip";
            var zipFilePath = Path.Combine(_backupRootPath, zipFileName);

            if (File.Exists(zipFilePath))
            {
                File.Delete(zipFilePath);
            }

            ZipFile.CreateFromDirectory(tempBackupFolder, zipFilePath, CompressionLevel.Optimal, false);

            // Clean up temporary folder
            Directory.Delete(tempBackupFolder, true);

            _logger.LogInformation("Backup completed successfully: {ZipFile}", zipFileName);

            // Upload to B2 if cloud storage is configured; otherwise serve from local disk
            if (_storage.IsCloud)
            {
                var b2Key = $"backups/{zipFileName}";
                await using var zipStream = File.OpenRead(zipFilePath);
                await _storage.UploadAsync(b2Key, zipStream);
                File.Delete(zipFilePath);
                _logger.LogInformation("Backup uploaded to B2: {Key}", b2Key);
                return b2Key;
            }

            return zipFilePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create backup for deployment {DeploymentId}", deploymentId);
            throw;
        }
    }

    public string GetBackupFilePath(int deploymentId)
    {
        // Find the most recent backup file for this deployment
        var backupFiles = Directory.GetFiles(_backupRootPath, $"Backup_Deployment_{deploymentId}_*.zip");
        
        if (backupFiles.Length == 0)
            throw new FileNotFoundException($"No backup file found for deployment {deploymentId}");

        // Return the most recent backup file
        return backupFiles.OrderByDescending(f => File.GetCreationTime(f)).First();
    }

    private string SanitizeFileName(string fileName)
    {
        // Remove invalid file name characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
        return sanitized;
    }
}




