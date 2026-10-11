using API.Data;
using API.DTOs.Responses;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using API.Utilities;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace API.Services.Implementations;

public class SchemaComparisonService : ISchemaComparisonService
{
    private readonly DeployVareDbContext _context;
    private readonly IEncryptionService _encryptionService;
    private readonly ISqlExecutionService _sqlExecutionService;
    private readonly ILogger<SchemaComparisonService> _logger;

    public SchemaComparisonService(
        DeployVareDbContext context,
        IEncryptionService encryptionService,
        ISqlExecutionService sqlExecutionService,
        ILogger<SchemaComparisonService> logger)
    {
        _context = context;
        _encryptionService = encryptionService;
        _sqlExecutionService = sqlExecutionService;
        _logger = logger;
    }

    public async Task<SchemaComparisonDto> CompareSchemaAsync(int sourceInstanceId, int targetInstanceId)
    {
        try
        {
            _logger.LogInformation("Comparing schemas: Source={SourceId}, Target={TargetId}", sourceInstanceId, targetInstanceId);

            // Get database instances
            var sourceInstance = await _context.DatabaseInstances
                .Include(d => d.Application)
                .FirstOrDefaultAsync(d => d.Id == sourceInstanceId)
                ?? throw new Exception($"Source instance {sourceInstanceId} not found");

            var targetInstance = await _context.DatabaseInstances
                .Include(d => d.Application)
                .FirstOrDefaultAsync(d => d.Id == targetInstanceId)
                ?? throw new Exception($"Target instance {targetInstanceId} not found");

            // Decrypt connection strings
            _logger.LogInformation("Decrypting connection strings...");
            var sourceConnectionString = _encryptionService.Decrypt(sourceInstance.ConnectionStringEncrypted);
            var targetConnectionString = _encryptionService.Decrypt(targetInstance.ConnectionStringEncrypted);

            // Get schemas from both databases
            _logger.LogInformation("Fetching source tables...");
            var sourceTables = await _sqlExecutionService.GetTablesAsync(sourceConnectionString);
            _logger.LogInformation("Found {Count} source tables", sourceTables.Count);

            _logger.LogInformation("Fetching target tables...");
            var targetTables = await _sqlExecutionService.GetTablesAsync(targetConnectionString);
            _logger.LogInformation("Found {Count} target tables", targetTables.Count);

            _logger.LogInformation("Fetching source procedures...");
            var sourceProcedures = await _sqlExecutionService.GetStoredProceduresAsync(sourceConnectionString);
            _logger.LogInformation("Found {Count} source procedures", sourceProcedures.Count);

            _logger.LogInformation("Fetching target procedures...");
            var targetProcedures = await _sqlExecutionService.GetStoredProceduresAsync(targetConnectionString);
            _logger.LogInformation("Found {Count} target procedures", targetProcedures.Count);

            _logger.LogInformation("Fetching source functions...");
            var sourceFunctions = await _sqlExecutionService.GetFunctionsAsync(sourceConnectionString);
            _logger.LogInformation("Found {Count} source functions", sourceFunctions.Count);

            _logger.LogInformation("Fetching target functions...");
            var targetFunctions = await _sqlExecutionService.GetFunctionsAsync(targetConnectionString);
            _logger.LogInformation("Found {Count} target functions", targetFunctions.Count);

            // Compare and generate diffs
            _logger.LogInformation("Comparing tables...");
            var tableDiffs = CompareTables(sourceTables, targetTables);
            _logger.LogInformation("Table comparison complete: {Count} differences", tableDiffs.Count);

            _logger.LogInformation("Comparing procedures...");
            var procedureDiffs = CompareProcedures(sourceProcedures, targetProcedures);
            _logger.LogInformation("Procedure comparison complete: {Count} differences", procedureDiffs.Count);

            _logger.LogInformation("Comparing functions...");
            var functionDiffs = CompareFunctions(sourceFunctions, targetFunctions);
            _logger.LogInformation("Function comparison complete: {Count} differences", functionDiffs.Count);

            var result = new SchemaComparisonDto
            {
                Source = new DatabaseSchemaInfo
                {
                    InstanceName = sourceInstance.InstanceName,
                    ApplicationName = sourceInstance.Application.Name,
                    CurrentPatchNumber = sourceInstance.CurrentPatchNumber
                },
                Target = new DatabaseSchemaInfo
                {
                    InstanceName = targetInstance.InstanceName,
                    ApplicationName = targetInstance.Application.Name,
                    CurrentPatchNumber = targetInstance.CurrentPatchNumber
                },
                TableDifferences = tableDiffs,
                ProcedureDifferences = procedureDiffs,
                FunctionDifferences = functionDiffs
            };

            _logger.LogInformation("Schema comparison completed successfully");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare schemas");
            throw;
        }
    }

    private List<SchemaObjectDiff> CompareTables(List<TableInfo> sourceTables, List<TableInfo> targetTables)
    {
        var differences = new List<SchemaObjectDiff>();

        var sourceTableDict = sourceTables.ToDictionary(t => $"{t.Schema}.{t.Name}");
        var targetTableDict = targetTables.ToDictionary(t => $"{t.Schema}.{t.Name}");

        // Tables in source but not in target (Added in source)
        foreach (var sourceTable in sourceTables)
        {
            var fullName = $"{sourceTable.Schema}.{sourceTable.Name}";
            if (!targetTableDict.ContainsKey(fullName))
            {
                differences.Add(new SchemaObjectDiff
                {
                    ObjectName = fullName,
                    DiffType = "Added",
                    SourceDefinition = FormatTableDefinition(sourceTable),
                    TargetDefinition = null,
                    Changes = new List<string> { $"+ Table '{fullName}' exists in source but not in target" }
                });
            }
        }

        // Tables in target but not in source (Removed from source)
        foreach (var targetTable in targetTables)
        {
            var fullName = $"{targetTable.Schema}.{targetTable.Name}";
            if (!sourceTableDict.ContainsKey(fullName))
            {
                differences.Add(new SchemaObjectDiff
                {
                    ObjectName = fullName,
                    DiffType = "Removed",
                    SourceDefinition = null,
                    TargetDefinition = FormatTableDefinition(targetTable),
                    Changes = new List<string> { $"- Table '{fullName}' exists in target but not in source" }
                });
            }
        }

        // Tables in both - compare columns
        foreach (var sourceTable in sourceTables)
        {
            var fullName = $"{sourceTable.Schema}.{sourceTable.Name}";
            if (targetTableDict.TryGetValue(fullName, out var targetTable))
            {
                var tableChanges = CompareTableColumns(sourceTable, targetTable);
                if (tableChanges.Any())
                {
                    differences.Add(new SchemaObjectDiff
                    {
                        ObjectName = fullName,
                        DiffType = "Modified",
                        SourceDefinition = FormatTableDefinition(sourceTable),
                        TargetDefinition = FormatTableDefinition(targetTable),
                        Changes = tableChanges
                    });
                }
                else
                {
                    differences.Add(new SchemaObjectDiff
                    {
                        ObjectName = fullName,
                        DiffType = "Identical",
                        SourceDefinition = FormatTableDefinition(sourceTable),
                        TargetDefinition = FormatTableDefinition(targetTable),
                        Changes = new List<string> { "= Table structure is identical" }
                    });
                }
            }
        }

        return differences;
    }

    private List<string> CompareTableColumns(TableInfo sourceTable, TableInfo targetTable)
    {
        var changes = new List<string>();

        var sourceColumns = sourceTable.Columns.ToDictionary(c => c.Name);
        var targetColumns = targetTable.Columns.ToDictionary(c => c.Name);

        // Columns added
        foreach (var sourceColumn in sourceTable.Columns)
        {
            if (!targetColumns.ContainsKey(sourceColumn.Name))
            {
                changes.Add($"+ Column '{sourceColumn.Name}' ({sourceColumn.DataType}) added");
            }
        }

        // Columns removed
        foreach (var targetColumn in targetTable.Columns)
        {
            if (!sourceColumns.ContainsKey(targetColumn.Name))
            {
                changes.Add($"- Column '{targetColumn.Name}' ({targetColumn.DataType}) removed");
            }
        }

        // Columns modified
        foreach (var sourceColumn in sourceTable.Columns)
        {
            if (targetColumns.TryGetValue(sourceColumn.Name, out var targetColumn))
            {
                if (sourceColumn.DataType != targetColumn.DataType)
                {
                    changes.Add($"~ Column '{sourceColumn.Name}' type changed: {targetColumn.DataType} → {sourceColumn.DataType}");
                }
                if (sourceColumn.IsNullable != targetColumn.IsNullable)
                {
                    var nullChange = sourceColumn.IsNullable ? "NOT NULL → NULL" : "NULL → NOT NULL";
                    changes.Add($"~ Column '{sourceColumn.Name}' nullable changed: {nullChange}");
                }
            }
        }

        return changes;
    }

    private List<SchemaObjectDiff> CompareProcedures(List<StoredProcedureInfo> sourceProcedures, List<StoredProcedureInfo> targetProcedures)
    {
        var differences = new List<SchemaObjectDiff>();
        int processedCount = 0;

        // OrdinalIgnoreCase prevents false "Added" when schema casing differs (e.g. dbo vs DBO)
        var sourceDict = sourceProcedures.ToDictionary(p => $"{p.Schema}.{p.Name}", StringComparer.OrdinalIgnoreCase);
        var targetDict = targetProcedures.ToDictionary(p => $"{p.Schema}.{p.Name}", StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation($"Comparing {sourceProcedures.Count} source procedures with {targetProcedures.Count} target procedures...");

        // Added procedures - NO DIFF GENERATION (too slow)
        foreach (var sourceProcedure in sourceProcedures)
        {
            var fullName = $"{sourceProcedure.Schema}.{sourceProcedure.Name}";
            if (!targetDict.ContainsKey(fullName))
            {
                differences.Add(new SchemaObjectDiff
                {
                    ObjectName = fullName,
                    DiffType = "Added",
                    SourceDefinition = sourceProcedure.Definition,
                    TargetDefinition = null,
                    Changes = new List<string> { $"+ Procedure added ({sourceProcedure.Definition?.Length ?? 0} characters)" }
                });
            }
        }
        _logger.LogInformation($"Found {differences.Count} added procedures");

        // Removed procedures - NO DIFF GENERATION (too slow)
        int removedCount = 0;
        foreach (var targetProcedure in targetProcedures)
        {
            var fullName = $"{targetProcedure.Schema}.{targetProcedure.Name}";
            if (!sourceDict.ContainsKey(fullName))
            {
                differences.Add(new SchemaObjectDiff
                {
                    ObjectName = fullName,
                    DiffType = "Removed",
                    SourceDefinition = null,
                    TargetDefinition = targetProcedure.Definition,
                    Changes = new List<string> { $"- Procedure removed ({targetProcedure.Definition?.Length ?? 0} characters)" }
                });
                removedCount++;
            }
        }
        _logger.LogInformation($"Found {removedCount} removed procedures");

        // Modified or identical procedures - Skip detailed diffs (too slow, even for 1 procedure)
        int modifiedCount = 0;
        int identicalCount = 0;
        const int MAX_DETAILED_DIFFS = 0; // DISABLED: DiffGenerator is too slow for production use
        
        _logger.LogInformation($"Checking for modified procedures (using hash comparison for speed)...");
        
        foreach (var sourceProcedure in sourceProcedures)
        {
            processedCount++;
            if (processedCount % 100 == 0)
            {
                _logger.LogInformation($"Processed {processedCount}/{sourceProcedures.Count} procedures ({identicalCount} identical, {modifiedCount} modified)...");
            }

            var fullName = $"{sourceProcedure.Schema}.{sourceProcedure.Name}";
            if (targetDict.TryGetValue(fullName, out var targetProcedure))
            {
                // FAST hash comparison instead of slow string comparison
                var sourceHash = ComputeHash(sourceProcedure.Definition);
                var targetHash = ComputeHash(targetProcedure.Definition);
                
                if (processedCount == 1)
                {
                    _logger.LogInformation($"First procedure hash comparison complete: {fullName}");
                }
                
                if (sourceHash == targetHash)
                {
                    // Identical - skip entirely (don't add to results to reduce payload)
                    identicalCount++;
                    continue;
                }
                else
                {
                    modifiedCount++;
                    
                    // Only generate detailed diff for first few modified procedures
                    List<string> changes;
                    if (modifiedCount <= MAX_DETAILED_DIFFS)
                    {
                        changes = GenerateLimitedDiff(targetProcedure.Definition, sourceProcedure.Definition, maxLines: 50);
                    }
                    else
                    {
                        // For remaining procedures, just show summary
                        var sourceLines = sourceProcedure.Definition?.Split('\n').Length ?? 0;
                        var targetLines = targetProcedure.Definition?.Split('\n').Length ?? 0;
                        changes = new List<string> 
                        { 
                            $"~ Procedure modified (Source: {sourceLines} lines, Target: {targetLines} lines)",
                            "  [Detailed diff omitted for performance - view full definitions below]"
                        };
                    }
                    
                    differences.Add(new SchemaObjectDiff
                    {
                        ObjectName = fullName,
                        DiffType = "Modified",
                        SourceDefinition = sourceProcedure.Definition,
                        TargetDefinition = targetProcedure.Definition,
                        Changes = changes
                    });
                }
            }
        }

        _logger.LogInformation($"Procedure comparison complete: {identicalCount} identical, {modifiedCount} modified (detailed diff for first {Math.Min(modifiedCount, MAX_DETAILED_DIFFS)})");

        return differences;
    }

    private List<SchemaObjectDiff> CompareFunctions(List<FunctionInfo> sourceFunctions, List<FunctionInfo> targetFunctions)
    {
        var differences = new List<SchemaObjectDiff>();

        // OrdinalIgnoreCase prevents false "Added" when schema casing differs (e.g. dbo vs DBO)
        var sourceDict = sourceFunctions.ToDictionary(f => $"{f.Schema}.{f.Name}", StringComparer.OrdinalIgnoreCase);
        var targetDict = targetFunctions.ToDictionary(f => $"{f.Schema}.{f.Name}", StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation($"Comparing {sourceFunctions.Count} source functions with {targetFunctions.Count} target functions...");

        // Added functions - NO DIFF GENERATION (for consistency)
        foreach (var sourceFunction in sourceFunctions)
        {
            var fullName = $"{sourceFunction.Schema}.{sourceFunction.Name}";
            if (!targetDict.ContainsKey(fullName))
            {
                differences.Add(new SchemaObjectDiff
                {
                    ObjectName = fullName,
                    DiffType = "Added",
                    SourceDefinition = sourceFunction.Definition,
                    TargetDefinition = null,
                    Changes = new List<string> { $"+ Function added ({sourceFunction.Definition?.Length ?? 0} characters)" }
                });
            }
        }

        // Removed functions - NO DIFF GENERATION (for consistency)
        foreach (var targetFunction in targetFunctions)
        {
            var fullName = $"{targetFunction.Schema}.{targetFunction.Name}";
            if (!sourceDict.ContainsKey(fullName))
            {
                differences.Add(new SchemaObjectDiff
                {
                    ObjectName = fullName,
                    DiffType = "Removed",
                    SourceDefinition = null,
                    TargetDefinition = targetFunction.Definition,
                    Changes = new List<string> { $"- Function removed ({targetFunction.Definition?.Length ?? 0} characters)" }
                });
            }
        }

        // Modified or identical functions
        int identicalCount = 0;
        int modifiedCount = 0;
        
        foreach (var sourceFunction in sourceFunctions)
        {
            var fullName = $"{sourceFunction.Schema}.{sourceFunction.Name}";
            if (targetDict.TryGetValue(fullName, out var targetFunction))
            {
                // FAST hash comparison instead of slow string comparison
                var sourceHash = ComputeHash(sourceFunction.Definition);
                var targetHash = ComputeHash(targetFunction.Definition);
                
                if (sourceHash == targetHash)
                {
                    // Identical - skip entirely
                    identicalCount++;
                    continue;
                }
                else
                {
                    // Modified - generate limited diff
                    modifiedCount++;
                    var diff = GenerateLimitedDiff(targetFunction.Definition, sourceFunction.Definition, maxLines: 50);
                    
                    differences.Add(new SchemaObjectDiff
                    {
                        ObjectName = fullName,
                        DiffType = "Modified",
                        SourceDefinition = sourceFunction.Definition,
                        TargetDefinition = targetFunction.Definition,
                        Changes = diff
                    });
                }
            }
        }

        _logger.LogInformation($"Function comparison complete: {identicalCount} identical, {modifiedCount} modified");

        return differences;
    }

    private List<string> GenerateLimitedDiff(string? oldText, string? newText, int maxLines = 100)
    {
        // AGGRESSIVE OPTIMIZATION: Skip diff generation for anything over 5KB (DiffGenerator is too slow)
        // Most procedures are large, and diff generation is O(n^2) which causes hangs
        if ((oldText?.Length ?? 0) > 5000 || (newText?.Length ?? 0) > 5000)
        {
            var oldLines = oldText?.Split('\n').Length ?? 0;
            var newLines = newText?.Split('\n').Length ?? 0;
            
            return new List<string> 
            { 
                "~ Procedure modified (detailed diff skipped for performance)",
                $"  Target: {oldText?.Length ?? 0} characters, {oldLines} lines",
                $"  Source: {newText?.Length ?? 0} characters, {newLines} lines",
                "  [View full definitions in Source/Target tabs below]"
            };
        }

        try
        {
            // Only generate diff for small procedures (< 5KB)
            var fullDiff = DiffGenerator.GenerateLineDiff(oldText, newText);

            // If diff is too large, truncate it
            if (fullDiff.Count > maxLines)
            {
                var limitedDiff = fullDiff.Take(maxLines).ToList();
                limitedDiff.Add($"... ({fullDiff.Count - maxLines} more lines omitted)");
                return limitedDiff;
            }

            return fullDiff;
        }
        catch (Exception)
        {
            // If diff generation fails, return summary
            return new List<string> 
            { 
                "~ Procedure modified (diff generation failed)",
                "  [View full definitions in Source/Target tabs below]"
            };
        }
    }

    private string FormatTableDefinition(TableInfo table)
    {
        var lines = new List<string>
        {
            $"CREATE TABLE [{table.Schema}].[{table.Name}] ("
        };

        for (int i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            var nullable = column.IsNullable ? "NULL" : "NOT NULL";
            var line = $"    [{column.Name}] {column.DataType} {nullable}";
            
            if (i < table.Columns.Count - 1)
                line += ",";
            
            lines.Add(line);
        }

        lines.Add(");");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Computes MD5 hash for fast comparison of procedure/function definitions.
    /// Much faster than string comparison for large text blocks.
    /// </summary>
    private string ComputeHash(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Normalize line endings and whitespace so that CRLF vs LF differences
        // (common between Windows source DB and Linux target DB) don't produce
        // false "Modified" results.
        var normalizedText = text
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Trim();

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(normalizedText));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Get detailed comparison for a specific procedure (lazy loading)
    /// </summary>
    public async Task<ProcedureDetailDto> GetProcedureDetailAsync(
        int sourceInstanceId, int targetInstanceId, string procedureName, string schema)
    {
        _logger.LogInformation("Getting procedure detail: {Schema}.{ProcedureName}", schema, procedureName);

        // Get database instances
        var sourceInstance = await _context.DatabaseInstances.FindAsync(sourceInstanceId)
            ?? throw new Exception($"Source instance {sourceInstanceId} not found");

        var targetInstance = await _context.DatabaseInstances.FindAsync(targetInstanceId)
            ?? throw new Exception($"Target instance {targetInstanceId} not found");

        // Decrypt connection strings
        var sourceConnectionString = _encryptionService.Decrypt(sourceInstance.ConnectionStringEncrypted);
        var targetConnectionString = _encryptionService.Decrypt(targetInstance.ConnectionStringEncrypted);

        // Fetch procedure definitions
        var sourceDefinition = await _sqlExecutionService.GetStoredProcedureDefinitionAsync(
            sourceConnectionString, procedureName, schema);
        var targetDefinition = await _sqlExecutionService.GetStoredProcedureDefinitionAsync(
            targetConnectionString, procedureName, schema);

        // Determine status
        var status = "Modified";
        if (string.IsNullOrEmpty(sourceDefinition) && !string.IsNullOrEmpty(targetDefinition))
            status = "Removed";
        else if (!string.IsNullOrEmpty(sourceDefinition) && string.IsNullOrEmpty(targetDefinition))
            status = "Added";

        // Generate diff.
        // Guard is based on the LCS cell count (lines × lines), not character count,
        // because LCS complexity is O(m*n) where m/n are line counts — not char counts.
        // A 50 KB procedure with 400 lines vs 200 lines = 80 000 cells: trivially fast.
        // Only skip the diff when the product would genuinely be expensive (>1M cells).
        string diff;
        var sourceLineCount = sourceDefinition?.Split('\n').Length ?? 0;
        var targetLineCount = targetDefinition?.Split('\n').Length ?? 0;
        if ((long)sourceLineCount * targetLineCount > 1_000_000)
        {
            diff = string.Join(Environment.NewLine, new[]
            {
                $"~ Definition too large for inline diff",
                $"  Source: {sourceDefinition?.Length ?? 0} chars / {sourceLineCount} lines",
                $"  Target: {targetDefinition?.Length ?? 0} chars / {targetLineCount} lines",
                "  [Scroll down to view full Source / Target definitions]"
            });
        }
        else
        {
            // old=target, new=source → green = source-only lines, red = target-only lines
            var diffLines = DiffGenerator.GenerateLineDiff(targetDefinition, sourceDefinition);
            diff = string.Join(Environment.NewLine, diffLines);
        }

        return new ProcedureDetailDto
        {
            ProcedureName = procedureName,
            Schema = schema,
            SourceDefinition = sourceDefinition,
            TargetDefinition = targetDefinition,
            Diff = diff,
            Status = status
        };
    }

    /// <summary>
    /// Transfer a database object (procedure/function) from source to target or vice versa
    /// </summary>
    public async Task<TransferResultDto> TransferObjectAsync(
        int sourceInstanceId, int targetInstanceId, string objectType, 
        string objectName, string schema, string direction, string transferredBy)
    {
        _logger.LogInformation(
            "Transferring {ObjectType} {Schema}.{ObjectName} - Direction: {Direction}",
            objectType, schema, objectName, direction);

        try
        {
            // Validate direction
            if (direction != "SourceToTarget" && direction != "TargetToSource")
                throw new ArgumentException("Direction must be 'SourceToTarget' or 'TargetToSource'");

            // Determine actual source and target based on direction
            var actualSourceId = direction == "SourceToTarget" ? sourceInstanceId : targetInstanceId;
            var actualTargetId = direction == "SourceToTarget" ? targetInstanceId : sourceInstanceId;

            // Get database instances
            var sourceInstance = await _context.DatabaseInstances.FindAsync(actualSourceId)
                ?? throw new Exception($"Source instance {actualSourceId} not found");

            var targetInstance = await _context.DatabaseInstances.FindAsync(actualTargetId)
                ?? throw new Exception($"Target instance {actualTargetId} not found");

            // Decrypt connection strings
            var sourceConnectionString = _encryptionService.Decrypt(sourceInstance.ConnectionStringEncrypted);
            var targetConnectionString = _encryptionService.Decrypt(targetInstance.ConnectionStringEncrypted);

            // Fetch source definition
            string sourceDefinition;
            if (objectType.Equals("StoredProcedure", StringComparison.OrdinalIgnoreCase) || 
                objectType.Equals("Procedure", StringComparison.OrdinalIgnoreCase))
            {
                sourceDefinition = await _sqlExecutionService.GetStoredProcedureDefinitionAsync(
                    sourceConnectionString, objectName, schema);
            }
            else if (objectType.Equals("Function", StringComparison.OrdinalIgnoreCase))
            {
                sourceDefinition = await _sqlExecutionService.GetFunctionDefinitionAsync(
                    sourceConnectionString, objectName, schema);
            }
            else
            {
                throw new ArgumentException($"Unsupported object type: {objectType}");
            }

            if (string.IsNullOrEmpty(sourceDefinition))
                throw new Exception($"Source {objectType} {schema}.{objectName} not found");

            // Transform script to CREATE OR ALTER (idempotent)
            var transformedScript = SqlScriptTransformer.TransformToCreateOrAlter(sourceDefinition);

            _logger.LogInformation("Transformed script for {Schema}.{ObjectName}", schema, objectName);

            // Create deployment record for audit trail
            var deployment = new Deployment
            {
                DatabaseInstanceId = actualTargetId,
                DeployedBy = transferredBy,
                DeployedAt = DateTime.UtcNow,
                Status = DeploymentStatus.InProgress,
                FromPatchNumber = 0,
                ToPatchNumber = 0
            };

            _context.Deployments.Add(deployment);
            await _context.SaveChangesAsync();

            // Create deployment object record
            var deploymentObject = new DeploymentObject
            {
                DeploymentId = deployment.Id,
                ObjectType = objectType,
                ObjectName = $"{schema}.{objectName}",
                BackedUp = false,
                ExecutedAt = DateTime.UtcNow
            };

            try
            {
                // Execute script on target database
                await _sqlExecutionService.ExecuteScriptAsync(targetConnectionString, transformedScript);

                // Update deployment status
                deployment.Status = DeploymentStatus.Success;
                deployment.DurationSeconds = 0;
                _context.DeploymentObjects.Add(deploymentObject);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Successfully transferred {Schema}.{ObjectName} to target", schema, objectName);

                return new TransferResultDto
                {
                    DeploymentId = deployment.Id,
                    Success = true,
                    Message = $"Successfully transferred {schema}.{objectName} to target database"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to transfer {Schema}.{ObjectName}", schema, objectName);

                // Update deployment status to failed
                deployment.Status = DeploymentStatus.Failed;
                deployment.ErrorMessage = ex.Message;
                deployment.DurationSeconds = 0;
                await _context.SaveChangesAsync();

                return new TransferResultDto
                {
                    DeploymentId = deployment.Id,
                    Success = false,
                    Message = "Transfer failed",
                    ErrorMessage = ex.Message
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Transfer operation failed");

            return new TransferResultDto
            {
                DeploymentId = 0,
                Success = false,
                Message = "Transfer failed",
                ErrorMessage = ex.Message
            };
        }
    }

    // ────────────────────────────────────────────────────────────────────────────
    // .dbsnap comparison
    // ────────────────────────────────────────────────────────────────────────────

    public async Task<SchemaComparisonDto> CompareWithSnapAsync(
        Stream? sourceSnapStream, string sourceSnapLabel,
        Stream? targetSnapStream, string targetSnapLabel,
        int? sourceInstanceId, int? targetInstanceId)
    {
        try
        {
            _logger.LogInformation(
                "CompareWithSnap: source={SrcLabel}/{SrcId}, target={TgtLabel}/{TgtId}",
                sourceSnapLabel, sourceInstanceId, targetSnapLabel, targetInstanceId);

            // ── Resolve SOURCE schema ──────────────────────────────────────────
            List<TableInfo> sourceTables;
            List<StoredProcedureInfo> sourceProcedures;
            List<FunctionInfo> sourceFunctions;
            DatabaseSchemaInfo sourceInfo;

            if (sourceSnapStream is not null)
            {
                var snap = await DbSnapReader.ReadAsync(sourceSnapStream);
                sourceTables = snap.Tables;
                sourceProcedures = snap.Procedures;
                sourceFunctions = snap.Functions;
                sourceInfo = new DatabaseSchemaInfo
                {
                    InstanceName = snap.DatabaseName,
                    ApplicationName = "Snapshot",
                    CurrentPatchNumber = 0
                };
            }
            else
            {
                var inst = await _context.DatabaseInstances
                    .Include(d => d.Application)
                    .FirstOrDefaultAsync(d => d.Id == sourceInstanceId)
                    ?? throw new Exception($"Source instance {sourceInstanceId} not found");
                var cs = _encryptionService.Decrypt(inst.ConnectionStringEncrypted);
                sourceTables = await _sqlExecutionService.GetTablesAsync(cs);
                sourceProcedures = await _sqlExecutionService.GetStoredProceduresAsync(cs);
                sourceFunctions = await _sqlExecutionService.GetFunctionsAsync(cs);
                sourceInfo = new DatabaseSchemaInfo
                {
                    InstanceName = inst.InstanceName,
                    ApplicationName = inst.Application.Name,
                    CurrentPatchNumber = inst.CurrentPatchNumber
                };
            }

            // ── Resolve TARGET schema ──────────────────────────────────────────
            List<TableInfo> targetTables;
            List<StoredProcedureInfo> targetProcedures;
            List<FunctionInfo> targetFunctions;
            DatabaseSchemaInfo targetInfo;

            if (targetSnapStream is not null)
            {
                var snap = await DbSnapReader.ReadAsync(targetSnapStream);
                targetTables = snap.Tables;
                targetProcedures = snap.Procedures;
                targetFunctions = snap.Functions;
                targetInfo = new DatabaseSchemaInfo
                {
                    InstanceName = snap.DatabaseName,
                    ApplicationName = "Snapshot",
                    CurrentPatchNumber = 0
                };
            }
            else
            {
                var inst = await _context.DatabaseInstances
                    .Include(d => d.Application)
                    .FirstOrDefaultAsync(d => d.Id == targetInstanceId)
                    ?? throw new Exception($"Target instance {targetInstanceId} not found");
                var cs = _encryptionService.Decrypt(inst.ConnectionStringEncrypted);
                targetTables = await _sqlExecutionService.GetTablesAsync(cs);
                targetProcedures = await _sqlExecutionService.GetStoredProceduresAsync(cs);
                targetFunctions = await _sqlExecutionService.GetFunctionsAsync(cs);
                targetInfo = new DatabaseSchemaInfo
                {
                    InstanceName = inst.InstanceName,
                    ApplicationName = inst.Application.Name,
                    CurrentPatchNumber = inst.CurrentPatchNumber
                };
            }

            // ── Compare ────────────────────────────────────────────────────────
            var tableDiffs = CompareTables(sourceTables, targetTables);
            var procedureDiffs = CompareProcedures(sourceProcedures, targetProcedures);
            var functionDiffs = CompareFunctions(sourceFunctions, targetFunctions);

            // Since both sides are in memory, generate real inline diffs for modified objects
            // (in a live DB-to-DB comparison these are lazy-loaded; here we have the data already).
            foreach (var diff in procedureDiffs.Where(d => d.DiffType == "Modified"))
                diff.Changes = GenerateLimitedDiff(diff.TargetDefinition, diff.SourceDefinition, 200);

            foreach (var diff in functionDiffs.Where(d => d.DiffType == "Modified"))
                diff.Changes = GenerateLimitedDiff(diff.TargetDefinition, diff.SourceDefinition, 200);

            _logger.LogInformation(
                "CompareWithSnap done: {T} table diffs, {P} proc diffs, {F} func diffs",
                tableDiffs.Count, procedureDiffs.Count, functionDiffs.Count);

            return new SchemaComparisonDto
            {
                Source = sourceInfo,
                Target = targetInfo,
                TableDifferences = tableDiffs,
                ProcedureDifferences = procedureDiffs,
                FunctionDifferences = functionDiffs
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CompareWithSnap failed");
            throw;
        }
    }
}

