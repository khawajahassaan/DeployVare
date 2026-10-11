using System.Text;
using System.Text.Json;
using API.Services.Interfaces;
using ZstdSharp;

namespace API.Utilities;

/// <summary>
/// Reads and parses .dbsnap files.
/// Format: 8-byte magic header (dbsnap\x00\x02) + zstd-compressed JSON payload.
/// </summary>
public static class DbSnapReader
{
    private static readonly byte[] Magic = { 0x64, 0x62, 0x73, 0x6E, 0x61, 0x70, 0x00, 0x02 };

    public static async Task<DbSnapSchema> ReadAsync(Stream stream)
    {
        // Validate magic header
        var header = new byte[8];
        var bytesRead = await ReadExactAsync(stream, header, 8);
        if (bytesRead < 8 || !header.SequenceEqual(Magic))
            throw new InvalidDataException("Not a valid .dbsnap file (invalid magic header).");

        // Decompress zstd payload
        byte[] jsonBytes;
        using (var ds = new DecompressionStream(stream))
        using (var ms = new MemoryStream())
        {
            await ds.CopyToAsync(ms);
            jsonBytes = ms.ToArray();
        }

        // Parse JSON
        using var doc = JsonDocument.Parse(jsonBytes);
        var root = doc.RootElement;

        // Extract string definition pool
        var defs = new List<string>();
        if (root.TryGetProperty("defs", out var defsEl) && defsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var d in defsEl.EnumerateArray())
                defs.Add(d.GetString() ?? string.Empty);
        }

        // Extract metadata
        var dbName = "Snapshot";
        var serverName = "Snapshot";
        if (root.TryGetProperty("meta", out var metaEl))
        {
            if (metaEl.TryGetProperty("database", out var dbEl)) dbName = dbEl.GetString() ?? dbName;
            if (metaEl.TryGetProperty("server", out var srvEl)) serverName = srvEl.GetString() ?? serverName;
        }

        // Extract tables
        var tables = new List<TableInfo>();
        if (root.TryGetProperty("tables", out var tablesEl) && tablesEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var tableProp in tablesEl.EnumerateObject())
            {
                var tableName = tableProp.Name;
                var tableVal = tableProp.Value;
                var schema = "dbo";
                if (tableVal.TryGetProperty("schema", out var schemaEl))
                    schema = schemaEl.GetString() ?? "dbo";

                var columns = new List<ColumnInfo>();
                if (tableVal.TryGetProperty("columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var col in colsEl.EnumerateArray())
                    {
                        var colName = col.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? "" : "";
                        var colType = col.TryGetProperty("type", out var tEl) ? tEl.GetString() ?? "" : "";
                        var nullable = !col.TryGetProperty("nullable", out var nullEl) || nullEl.GetBoolean();
                        int? maxLength = null;
                        if (col.TryGetProperty("maxLength", out var mlEl) && mlEl.ValueKind == JsonValueKind.Number)
                            maxLength = mlEl.GetInt32();

                        columns.Add(new ColumnInfo
                        {
                            Name = colName,
                            DataType = colType,
                            IsNullable = nullable,
                            MaxLength = maxLength
                        });
                    }
                }

                tables.Add(new TableInfo { Name = tableName, Schema = schema, Columns = columns });
            }
        }

        // Extract procedures
        var procedures = new List<StoredProcedureInfo>();
        if (root.TryGetProperty("procedures", out var procsEl) && procsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var procProp in procsEl.EnumerateObject())
            {
                var (schema, name) = SplitSchemaName(procProp.Name);
                var definition = ResolveDefinition(procProp.Value, defs);
                procedures.Add(new StoredProcedureInfo { Name = name, Schema = schema, Definition = definition });
            }
        }

        // Extract triggers — treat as procedures
        if (root.TryGetProperty("triggers", out var trigsEl) && trigsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var trigProp in trigsEl.EnumerateObject())
            {
                var (schema, name) = SplitSchemaName(trigProp.Name);
                var definition = ResolveDefinition(trigProp.Value, defs);
                procedures.Add(new StoredProcedureInfo { Name = name, Schema = schema, Definition = definition });
            }
        }

        // Extract functions
        var functions = new List<FunctionInfo>();
        if (root.TryGetProperty("functions", out var funcsEl) && funcsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var funcProp in funcsEl.EnumerateObject())
            {
                var (schema, name) = SplitSchemaName(funcProp.Name);
                var definition = ResolveDefinition(funcProp.Value, defs);
                functions.Add(new FunctionInfo { Name = name, Schema = schema, Definition = definition });
            }
        }

        return new DbSnapSchema(dbName, serverName, tables, procedures, functions);
    }

    // ---------- helpers ----------

    private static string ResolveDefinition(JsonElement element, List<string> defs)
    {
        // The definition field may be an integer index into defs[], or a raw string
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("definition", out var defEl))
            {
                if (defEl.ValueKind == JsonValueKind.Number)
                {
                    var idx = defEl.GetInt32();
                    return idx >= 0 && idx < defs.Count ? defs[idx] : string.Empty;
                }
                if (defEl.ValueKind == JsonValueKind.String)
                    return defEl.GetString() ?? string.Empty;
            }
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            var idx = element.GetInt32();
            return idx >= 0 && idx < defs.Count ? defs[idx] : string.Empty;
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static (string schema, string name) SplitSchemaName(string key)
    {
        var dot = key.IndexOf('.');
        if (dot >= 0)
            return (key[..dot], key[(dot + 1)..]);
        return ("dbo", key);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var n = await stream.ReadAsync(buffer, totalRead, count - totalRead);
            if (n == 0) break;
            totalRead += n;
        }
        return totalRead;
    }

    /// <summary>
    /// Quick sanity test: reads a .dbsnap file and prints what was found.
    /// Call via: DbSnapReader.TestRead("/path/to/file.dbsnap")
    /// </summary>
    public static async Task TestReadAsync(string filePath)
    {
        Console.WriteLine($"[DbSnapReader.TestRead] Reading: {filePath}");
        await using var fs = File.OpenRead(filePath);
        var schema = await ReadAsync(fs);
        Console.WriteLine($"  Database : {schema.DatabaseName}");
        Console.WriteLine($"  Server   : {schema.ServerName}");
        Console.WriteLine($"  Tables   : {schema.Tables.Count}");
        Console.WriteLine($"  Procs    : {schema.Procedures.Count}");
        Console.WriteLine($"  Functions: {schema.Functions.Count}");
        Console.WriteLine("--- Tables ---");
        foreach (var t in schema.Tables.Take(5))
            Console.WriteLine($"  [{t.Schema}].[{t.Name}] ({t.Columns.Count} cols)");
        if (schema.Tables.Count > 5) Console.WriteLine($"  ... and {schema.Tables.Count - 5} more");
        Console.WriteLine("--- Procedures ---");
        foreach (var p in schema.Procedures.Take(5))
            Console.WriteLine($"  [{p.Schema}].[{p.Name}] — {p.Definition.Length} chars");
        if (schema.Procedures.Count > 5) Console.WriteLine($"  ... and {schema.Procedures.Count - 5} more");
    }
}

/// <summary>Parsed content of a .dbsnap file.</summary>
public record DbSnapSchema(
    string DatabaseName,
    string ServerName,
    List<TableInfo> Tables,
    List<StoredProcedureInfo> Procedures,
    List<FunctionInfo> Functions
);
