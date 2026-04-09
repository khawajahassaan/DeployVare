using Dapper;
using Microsoft.Data.SqlClient;
using API.Services.Interfaces;

namespace API.Services.Implementations;

public class SqlExecutionService : ISqlExecutionService
{
    private readonly ILogger<SqlExecutionService> _logger;

    public SqlExecutionService(ILogger<SqlExecutionService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(string connectionString)
    {
        try
        {
            using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed");
            return false;
        }
    }

    public async Task ExecuteScriptAsync(string connectionString, string script)
    {
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        // Split script by GO statements
        var batches = script.Split(new[] { "\nGO\n", "\nGO\r\n", "\r\nGO\r\n" }, 
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var batch in batches)
        {
            if (string.IsNullOrWhiteSpace(batch))
                continue;

            try
            {
                await connection.ExecuteAsync(batch, commandTimeout: 300);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing batch: {Batch}", batch.Substring(0, Math.Min(100, batch.Length)));
                throw;
            }
        }
    }

    public async Task<List<StoredProcedureInfo>> GetStoredProceduresAsync(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);

        const string query = @"
            SELECT 
                ROUTINE_SCHEMA as [Schema],
                ROUTINE_NAME as [Name],
                ROUTINE_DEFINITION as [Definition]
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'PROCEDURE'
            ORDER BY ROUTINE_NAME";

        var results = await connection.QueryAsync<StoredProcedureInfo>(query);
        return results.ToList();
    }

    public async Task<List<FunctionInfo>> GetFunctionsAsync(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);

        const string query = @"
            SELECT 
                ROUTINE_SCHEMA as [Schema],
                ROUTINE_NAME as [Name],
                ROUTINE_DEFINITION as [Definition]
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'FUNCTION'
            ORDER BY ROUTINE_NAME";

        var results = await connection.QueryAsync<FunctionInfo>(query);
        return results.ToList();
    }

    public async Task<string> GetStoredProcedureDefinitionAsync(string connectionString, string procedureName, string schema = "dbo")
    {
        using var connection = new SqlConnection(connectionString);

        const string query = @"
            SELECT ROUTINE_DEFINITION
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'PROCEDURE' 
              AND ROUTINE_SCHEMA = @Schema
              AND ROUTINE_NAME = @ProcedureName";

        var definition = await connection.QueryFirstOrDefaultAsync<string>(query, new { Schema = schema, ProcedureName = procedureName });
        return definition ?? string.Empty;
    }

    public async Task<string> GetFunctionDefinitionAsync(string connectionString, string functionName, string schema = "dbo")
    {
        using var connection = new SqlConnection(connectionString);

        const string query = @"
            SELECT ROUTINE_DEFINITION
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'FUNCTION' 
              AND ROUTINE_SCHEMA = @Schema
              AND ROUTINE_NAME = @FunctionName";

        var definition = await connection.QueryFirstOrDefaultAsync<string>(query, new { Schema = schema, FunctionName = functionName });
        return definition ?? string.Empty;
    }

    public async Task<List<TableInfo>> GetTablesAsync(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);

        const string query = @"
            SELECT 
                t.TABLE_SCHEMA as [Schema],
                t.TABLE_NAME as [Name],
                c.COLUMN_NAME as ColumnName,
                c.DATA_TYPE as DataType,
                c.IS_NULLABLE as IsNullable,
                c.CHARACTER_MAXIMUM_LENGTH as MaxLength
            FROM INFORMATION_SCHEMA.TABLES t
            LEFT JOIN INFORMATION_SCHEMA.COLUMNS c ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
            WHERE t.TABLE_TYPE = 'BASE TABLE'
            ORDER BY t.TABLE_NAME, c.ORDINAL_POSITION";

        var results = await connection.QueryAsync<dynamic>(query);
        
        var tables = new Dictionary<string, TableInfo>();
        foreach (var row in results)
        {
            string tableName = row.Name;
            if (!tables.ContainsKey(tableName))
            {
                tables[tableName] = new TableInfo
                {
                    Name = tableName,
                    Schema = row.Schema,
                    Columns = new List<ColumnInfo>()
                };
            }

            if (row.ColumnName != null)
            {
                tables[tableName].Columns.Add(new ColumnInfo
                {
                    Name = row.ColumnName,
                    DataType = row.DataType,
                    IsNullable = row.IsNullable == "YES",
                    MaxLength = row.MaxLength
                });
            }
        }

        return tables.Values.ToList();
    }
}
