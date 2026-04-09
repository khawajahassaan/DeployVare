namespace API.DTOs.Responses;

public class SchemaComparisonDto
{
    public DatabaseSchemaInfo Source { get; set; } = new();
    public DatabaseSchemaInfo Target { get; set; } = new();
    public List<SchemaObjectDiff> TableDifferences { get; set; } = new();
    public List<SchemaObjectDiff> ProcedureDifferences { get; set; } = new();
    public List<SchemaObjectDiff> FunctionDifferences { get; set; } = new();
}

public class DatabaseSchemaInfo
{
    public string InstanceName { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public int CurrentPatchNumber { get; set; }
}

public class SchemaObjectDiff
{
    public string ObjectName { get; set; } = string.Empty;
    public string DiffType { get; set; } = string.Empty; // Added, Removed, Modified, Identical
    public string? SourceDefinition { get; set; }
    public string? TargetDefinition { get; set; }
    public List<string> Changes { get; set; } = new(); // Detailed changes for git-like diff
}



