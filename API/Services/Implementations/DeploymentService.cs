using API.Data;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using API.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.IO.Compression;

namespace API.Services.Implementations;

public class DeploymentService : IDeploymentService
{
    private readonly DeployVareDbContext _context;
    private readonly IEncryptionService _encryptionService;
    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly IBackupService _backupService;
    private readonly ILogger<DeploymentService> _logger;

    public DeploymentService(
        DeployVareDbContext context,
        IEncryptionService encryptionService,
        ISqlExecutionService sqlExecutionService,
        IBackupService backupService,
        ILogger<DeploymentService> logger)
    {
        _context = context;
        _encryptionService = encryptionService;
        _sqlExecutionService = sqlExecutionService;
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<DeploymentResultDto> DeployAsync(int databaseInstanceId, string zipFilePath, string deployedBy)
    {
        var stopwatch = Stopwatch.StartNew();
        Deployment? deployment = null;

        try
        {
            _logger.LogInformation("Starting deployment for DatabaseInstance {InstanceId}", databaseInstanceId);

            // Get database instance
            var dbInstance = await _context.DatabaseInstances
                .Include(d => d.Application)
                .FirstOrDefaultAsync(d => d.Id == databaseInstanceId)
                ?? throw new Exception($"Database instance {databaseInstanceId} not found");

            // Decrypt connection string
            var connectionString = _encryptionService.Decrypt(dbInstance.ConnectionStringEncrypted);

            // Test connection
            if (!await _sqlExecutionService.TestConnectionAsync(connectionString))
            {
                throw new Exception("Failed to connect to target database");
            }

            // Extract ZIP file
            var extractPath = ExtractZipFile(zipFilePath);
            
            try
            {
                // Parse SQL scripts
                var sqlPatches = ParseSqlPatches(extractPath);
                var procedures = ParseStoredProcedures(extractPath);
                var functions = ParseFunctions(extractPath);

                // Determine patches to apply
                var currentPatchNumber = dbInstance.CurrentPatchNumber;
                var patchesToApply = sqlPatches
                    .Where(p => p.PatchNumber > currentPatchNumber)
                    .OrderBy(p => p.PatchNumber)
                    .ToList();

                var latestPatchNumber = patchesToApply.Any() 
                    ? patchesToApply.Max(p => p.PatchNumber) 
                    : currentPatchNumber;

                _logger.LogInformation(
                    "Current patch: {CurrentPatch}, Latest patch: {LatestPatch}, Patches to apply: {Count}",
                    currentPatchNumber, latestPatchNumber, patchesToApply.Count);

                // Create deployment record
                deployment = new Deployment
                {
                    DatabaseInstanceId = databaseInstanceId,
                    DeployedBy = deployedBy,
                    DeployedAt = DateTime.UtcNow,
                    Status = DeploymentStatus.InProgress,
                    FromPatchNumber = currentPatchNumber,
                    ToPatchNumber = latestPatchNumber
                };

                _context.Deployments.Add(deployment);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Deployment {DeploymentId} created", deployment.Id);

                // Backup stored procedures and functions
                var backupFilePath = string.Empty;
                if (procedures.Any() || functions.Any())
                {
                    _logger.LogInformation("Creating backup of existing procedures and functions");
                    var procedureNames = procedures.Select(p => p.Name).ToList();
                    var functionNames = functions.Select(f => f.Name).ToList();
                    
                    backupFilePath = await _backupService.BackupDatabaseObjectsAsync(
                        connectionString, procedureNames, functionNames, deployment.Id);
                    
                    deployment.BackupFilePath = backupFilePath;
                    await _context.SaveChangesAsync();
                    
                    _logger.LogInformation("Backup created: {BackupPath}", backupFilePath);
                }

                // ===== PHASE 1: Apply SQL Patches (Critical DDL) =====
                foreach (var patch in patchesToApply)
                {
                    _logger.LogInformation("Applying patch {PatchNumber}: {FileName}", patch.PatchNumber, patch.FileName);

                    var patchRecord = new DeploymentPatch
                    {
                        DeploymentId = deployment.Id,
                        PatchNumber = patch.PatchNumber,
                        PatchFileName = patch.FileName,
                        Description = patch.Description,
                        Author = patch.Author,
                        Status = "Success",
                        ExecutedAt = DateTime.UtcNow
                    };

                    try
                    {
                        await _sqlExecutionService.ExecuteScriptAsync(connectionString, patch.Script);
                        _logger.LogInformation("✓ Patch {PatchNumber} applied successfully", patch.PatchNumber);
                    }
                    catch (Exception ex)
                    {
                        patchRecord.Status = "Failed";
                        patchRecord.ErrorMessage = ex.Message;
                        _logger.LogError(ex, "✗ Failed to apply patch {PatchNumber}", patch.PatchNumber);
                        
                        _context.DeploymentPatches.Add(patchRecord);
                        await _context.SaveChangesAsync();
                        
                        throw new Exception($"Patch {patch.PatchNumber} failed: {ex.Message}", ex);
                    }

                    _context.DeploymentPatches.Add(patchRecord);
                    await _context.SaveChangesAsync();
                }

                // ===== UPDATE PATCH NUMBER IMMEDIATELY AFTER PATCHES SUCCEED =====
                if (patchesToApply.Any())
                {
                    dbInstance.CurrentPatchNumber = latestPatchNumber;
                    dbInstance.UpdatedAt = DateTime.UtcNow;
                    dbInstance.UpdatedBy = deployedBy;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("✓ Database instance patch number updated to {PatchNumber}", latestPatchNumber);
                }

                // Track procedure/function errors without failing deployment
                var procedureErrors = new List<string>();
                var functionErrors = new List<string>();
                int proceduresSucceeded = 0;
                int functionsSucceeded = 0;

                // ===== PHASE 2: Apply Stored Procedures (Safe, Idempotent) =====
                foreach (var procedure in procedures)
                {
                    _logger.LogInformation("Applying stored procedure: {ProcedureName}", procedure.Name);

                    var objectRecord = new DeploymentObject
                    {
                        DeploymentId = deployment.Id,
                        ObjectType = "StoredProcedure",
                        ObjectName = procedure.Name,
                        BackedUp = !string.IsNullOrEmpty(backupFilePath)
                    };

                    try
                    {
                        await _sqlExecutionService.ExecuteScriptAsync(connectionString, procedure.Script);
                        _logger.LogInformation("✓ Procedure {ProcedureName} applied successfully", procedure.Name);
                        proceduresSucceeded++;
                        _context.DeploymentObjects.Add(objectRecord);
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "✗ Failed to apply procedure {ProcedureName}", procedure.Name);
                        procedureErrors.Add($"{procedure.Name}: {ex.Message}");
                    }
                }

                // ===== PHASE 3: Apply Functions (Safe, Idempotent) =====
                foreach (var function in functions)
                {
                    _logger.LogInformation("Applying function: {FunctionName}", function.Name);

                    var objectRecord = new DeploymentObject
                    {
                        DeploymentId = deployment.Id,
                        ObjectType = "Function",
                        ObjectName = function.Name,
                        BackedUp = !string.IsNullOrEmpty(backupFilePath)
                    };

                    try
                    {
                        await _sqlExecutionService.ExecuteScriptAsync(connectionString, function.Script);
                        _logger.LogInformation("✓ Function {FunctionName} applied successfully", function.Name);
                        functionsSucceeded++;
                        _context.DeploymentObjects.Add(objectRecord);
                        await _context.SaveChangesAsync();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "✗ Failed to apply function {FunctionName}", function.Name);
                        functionErrors.Add($"{function.Name}: {ex.Message}");
                    }
                }

                // ===== Determine Final Status =====
                stopwatch.Stop();
                var hasErrors = procedureErrors.Any() || functionErrors.Any();
                
                if (hasErrors)
                {
                    // Patches succeeded, but procedures/functions had issues
                    deployment.Status = DeploymentStatus.PartialSuccess;
                    var errorMsg = new List<string>();
                    if (procedureErrors.Any())
                        errorMsg.Add($"Procedure errors: {string.Join("; ", procedureErrors)}");
                    if (functionErrors.Any())
                        errorMsg.Add($"Function errors: {string.Join("; ", functionErrors)}");
                    deployment.ErrorMessage = string.Join(" | ", errorMsg);
                    
                    _logger.LogWarning("Deployment {DeploymentId} completed with warnings. Patches applied: {PatchCount}, Procedures: {ProcSuccess}/{ProcTotal}, Functions: {FuncSuccess}/{FuncTotal}", 
                        deployment.Id, patchesToApply.Count, proceduresSucceeded, procedures.Count, functionsSucceeded, functions.Count);
                }
                else
                {
                    // Everything succeeded
                    deployment.Status = DeploymentStatus.Success;
                    _logger.LogInformation("Deployment {DeploymentId} completed successfully in {Duration}s", 
                        deployment.Id, stopwatch.Elapsed.TotalSeconds);
                }

                deployment.DurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
                await _context.SaveChangesAsync();

                return new DeploymentResultDto
                {
                    DeploymentId = deployment.Id,
                    Success = !hasErrors,
                    Message = hasErrors 
                        ? $"Deployment partially successful. Patches applied: {patchesToApply.Count}. Procedures: {proceduresSucceeded}/{procedures.Count}. Functions: {functionsSucceeded}/{functions.Count}."
                        : "Deployment completed successfully",
                    FromPatchNumber = currentPatchNumber,
                    ToPatchNumber = latestPatchNumber,
                    PatchesApplied = patchesToApply.Count,
                    ProceduresUpdated = proceduresSucceeded,
                    FunctionsUpdated = functionsSucceeded,
                    BackupFileName = !string.IsNullOrEmpty(backupFilePath) ? Path.GetFileName(backupFilePath) : null,
                    DurationSeconds = deployment.DurationSeconds.Value,
                    ErrorMessage = hasErrors ? deployment.ErrorMessage : null
                };
            }
            finally
            {
                // Clean up extracted files
                if (Directory.Exists(extractPath))
                {
                    Directory.Delete(extractPath, true);
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Deployment failed");

            if (deployment != null)
            {
                deployment.Status = DeploymentStatus.Failed;
                deployment.ErrorMessage = ex.Message;
                deployment.DurationSeconds = (int)stopwatch.Elapsed.TotalSeconds;
                await _context.SaveChangesAsync();
            }

            return new DeploymentResultDto
            {
                DeploymentId = deployment?.Id ?? 0,
                Success = false,
                Message = "Deployment failed",
                ErrorMessage = ex.Message,
                DurationSeconds = (int)stopwatch.Elapsed.TotalSeconds
            };
        }
    }

    private string ExtractZipFile(string zipFilePath)
    {
        var extractPath = Path.Combine(Path.GetTempPath(), $"deploy_{Guid.NewGuid()}");
        Directory.CreateDirectory(extractPath);

        _logger.LogInformation("Extracting ZIP file to: {ExtractPath}", extractPath);
        ZipFile.ExtractToDirectory(zipFilePath, extractPath);

        return extractPath;
    }

    private List<SqlPatchInfo> ParseSqlPatches(string extractPath)
    {
        var patches = new List<SqlPatchInfo>();
        var patchesFolder = Path.Combine(extractPath, "SQLPatch");

        if (!Directory.Exists(patchesFolder))
        {
            _logger.LogWarning("SQLPatch folder not found in ZIP");
            return patches;
        }

        var patchFiles = Directory.GetFiles(patchesFolder, "*.sql");

        foreach (var filePath in patchFiles)
        {
            var fileName = Path.GetFileName(filePath);
            var patch = SqlPatchParser.ParsePatchFileName(fileName);

            if (patch != null)
            {
                patch.Script = File.ReadAllText(filePath);
                patches.Add(patch);
                _logger.LogInformation("Found patch: {PatchNumber} - {Description} by {Author}", 
                    patch.PatchNumber, patch.Description, patch.Author);
            }
            else
            {
                _logger.LogWarning("Failed to parse patch filename: {FileName}", fileName);
            }
        }

        return patches.OrderBy(p => p.PatchNumber).ToList();
    }

    private List<SqlScriptInfo> ParseStoredProcedures(string extractPath)
    {
        var procedures = new List<SqlScriptInfo>();
        var proceduresFolder = Path.Combine(extractPath, "StoreProcedure");

        if (!Directory.Exists(proceduresFolder))
        {
            _logger.LogWarning("StoreProcedure folder not found in ZIP");
            return procedures;
        }

        var procedureFiles = Directory.GetFiles(proceduresFolder, "*.sql");

        foreach (var filePath in procedureFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var script = File.ReadAllText(filePath);

            procedures.Add(new SqlScriptInfo
            {
                Name = fileName,
                Script = script
            });

            _logger.LogInformation("Found stored procedure: {ProcedureName}", fileName);
        }

        return procedures;
    }

    private List<SqlScriptInfo> ParseFunctions(string extractPath)
    {
        var functions = new List<SqlScriptInfo>();
        var functionsFolder = Path.Combine(extractPath, "SQLFunction");

        if (!Directory.Exists(functionsFolder))
        {
            _logger.LogWarning("SQLFunction folder not found in ZIP");
            return functions;
        }

        var functionFiles = Directory.GetFiles(functionsFolder, "*.sql");

        foreach (var filePath in functionFiles)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var script = File.ReadAllText(filePath);

            functions.Add(new SqlScriptInfo
            {
                Name = fileName,
                Script = script
            });

            _logger.LogInformation("Found function: {FunctionName}", fileName);
        }

        return functions;
    }
}



