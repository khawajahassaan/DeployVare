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

        // INFORMATION_SCHEMA.ROUTINES for the list: guarantees all procedure names are
        // returned even when the connection user only has EXECUTE (not VIEW DEFINITION).
        // sys.sql_modules would silently drop those rows, making them appear as "Source Only".
        // OBJECT_DEFINITION() returns the full untruncated text (no 4000-char limit) where
        // VIEW DEFINITION is granted, and NULL otherwise — keeping the row in the result.
        const string query = @"
            SELECT 
                ROUTINE_SCHEMA AS [Schema],
                ROUTINE_NAME AS [Name],
                OBJECT_DEFINITION(OBJECT_ID(
                    QUOTENAME(ROUTINE_SCHEMA) + '.' + QUOTENAME(ROUTINE_NAME)
                )) AS [Definition]
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'PROCEDURE'
            ORDER BY ROUTINE_NAME";

        var results = await connection.QueryAsync<StoredProcedureInfo>(query);
        return results.ToList();
    }

    public async Task<List<FunctionInfo>> GetFunctionsAsync(string connectionString)
    {
        using var connection = new SqlConnection(connectionString);

        // INFORMATION_SCHEMA.ROUTINES for the list — same reason as GetStoredProceduresAsync.
        // OBJECT_DEFINITION() for full untruncated definition without dropping permission-limited rows.
        const string query = @"
            SELECT 
                ROUTINE_SCHEMA AS [Schema],
                ROUTINE_NAME AS [Name],
                OBJECT_DEFINITION(OBJECT_ID(
                    QUOTENAME(ROUTINE_SCHEMA) + '.' + QUOTENAME(ROUTINE_NAME)
                )) AS [Definition]
            FROM INFORMATION_SCHEMA.ROUTINES
            WHERE ROUTINE_TYPE = 'FUNCTION'
            ORDER BY ROUTINE_NAME";

        var results = await connection.QueryAsync<FunctionInfo>(query);
        return results.ToList();
    }

    public async Task<string> GetStoredProcedureDefinitionAsync(string connectionString, string procedureName, string schema = "dbo")
    {
        using var connection = new SqlConnection(connectionString);

        // sys.sql_modules.definition is nvarchar(MAX) — no 4000-char truncation
        const string query = @"
            SELECT m.definition
            FROM sys.sql_modules m
            JOIN sys.objects o ON o.object_id = m.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type = 'P'
              AND s.name = @Schema
              AND o.name = @ProcedureName";

        var definition = await connection.QueryFirstOrDefaultAsync<string>(query, new { Schema = schema, ProcedureName = procedureName });
        return definition ?? string.Empty;
    }

    public async Task<string> GetFunctionDefinitionAsync(string connectionString, string functionName, string schema = "dbo")
    {
        using var connection = new SqlConnection(connectionString);

        // sys.sql_modules.definition is nvarchar(MAX) — no 4000-char truncation
        const string query = @"
            SELECT m.definition
            FROM sys.sql_modules m
            JOIN sys.objects o ON o.object_id = m.object_id
            JOIN sys.schemas s ON s.schema_id = o.schema_id
            WHERE o.type IN ('FN', 'IF', 'TF')
              AND s.name = @Schema
              AND o.name = @FunctionName";

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
