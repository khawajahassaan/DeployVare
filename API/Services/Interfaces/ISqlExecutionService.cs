namespace API.Services.Interfaces;

public interface ISqlExecutionService
{
    Task<bool> TestConnectionAsync(string connectionString);
    Task ExecuteScriptAsync(string connectionString, string script);
    Task<List<StoredProcedureInfo>> GetStoredProceduresAsync(string connectionString);
    Task<List<FunctionInfo>> GetFunctionsAsync(string connectionString);
    Task<string> GetStoredProcedureDefinitionAsync(string connectionString, string procedureName, string schema = "dbo");
    Task<string> GetFunctionDefinitionAsync(string connectionString, string functionName, string schema = "dbo");
    Task<List<TableInfo>> GetTablesAsync(string connectionString);
}

public class StoredProcedureInfo
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public string Definition { get; set; } = string.Empty;
}

public class FunctionInfo
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public string Definition { get; set; } = string.Empty;
}

public class TableInfo
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = "dbo";
    public List<ColumnInfo> Columns { get; set; } = new();
}

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public int? MaxLength { get; set; }
}



