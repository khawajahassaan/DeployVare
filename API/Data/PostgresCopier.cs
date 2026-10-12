using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

namespace API.Data;

/// <summary>
/// One-time move of an existing DeployVare database from SQL Server to PostgreSQL (e.g. Supabase), exactly as
/// it is: same rows, same ids, same password hashes and encrypted values. Runs instead of the web host:
///
///   dotnet run -- copy-to-postgres --source "&lt;SQL Server connection string&gt;" --target "&lt;Npgsql connection string&gt;" [--replace]
///
/// The target gets the Postgres migrations first. Tables are copied parents-first along the foreign keys,
/// identity sequences are moved past the copied ids, and row counts are compared at the end. A target that
/// already has rows is refused unless --replace is given (which empties the DeployVare tables first).
/// The application must use the same EncryptionSettings:EncryptionKey afterwards, or the copied secrets
/// (connection strings, SSH passwords, the Azure DevOps token) cannot be decrypted.
/// </summary>
public static class PostgresCopier
{
    public static async Task<int> RunAsync(string[] args)
    {
        string? Arg(string name)
        {
            var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        var source = Arg("--source") ?? Environment.GetEnvironmentVariable("SOURCE_CONNECTION_STRING");
        var target = Arg("--target") ?? Environment.GetEnvironmentVariable("TARGET_CONNECTION_STRING");
        var replace = args.Any(a => a.Equals("--replace", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("Usage: copy-to-postgres --source \"<SQL Server connection string>\" --target \"<Npgsql connection string>\" [--replace]");
            Console.Error.WriteLine("       (or set SOURCE_CONNECTION_STRING / TARGET_CONNECTION_STRING)");
            return 2;
        }

        try
        {
            await CopyAsync(source, target, replace);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Copy failed: {ex.Message}");
            return 1;
        }
    }

    private record TableMap(string Table, string? Schema, List<(string Column, Type ClrType)> Columns, string? IdentityColumn);

    public static async Task CopyAsync(string sourceConnectionString, string targetConnectionString, bool replace)
    {
        PostgresSetup.ConfigureNpgsql();

        var options = new DbContextOptionsBuilder<PostgresDeployVareDbContext>().UseNpgsql(targetConnectionString).Options;
        await using var context = new PostgresDeployVareDbContext(options);

        Console.WriteLine("Applying the PostgreSQL migrations to the target...");
        await context.Database.MigrateAsync();

        var tables = OrderParentsFirst(context.Model);

        await using var sql = new SqlConnection(sourceConnectionString);
        await sql.OpenAsync();
        await using var pg = new NpgsqlConnection(targetConnectionString);
        await pg.OpenAsync();

        // Refuse to merge into a database that already has data
        var occupied = new List<string>();
        foreach (var t in tables)
        {
            var count = System.Convert.ToInt64(await new NpgsqlCommand($"SELECT COUNT(*) FROM {Pg(t)}", pg).ExecuteScalarAsync());
            if (count > 0) occupied.Add($"{t.Table} ({count})");
        }
        if (occupied.Count > 0 && !replace)
            throw new InvalidOperationException(
                $"The target already has rows in {string.Join(", ", occupied)}. Nothing was copied. " +
                "Run again with --replace to empty the DeployVare tables on the target first.");

        await using var tx = await pg.BeginTransactionAsync();
        if (occupied.Count > 0)
        {
            Console.WriteLine("Emptying the DeployVare tables on the target (--replace)...");
            await new NpgsqlCommand($"TRUNCATE {string.Join(", ", tables.Select(Pg))} RESTART IDENTITY CASCADE", pg, tx).ExecuteNonQueryAsync();
        }

        var summary = new List<(string Table, long Source, long Copied)>();
        long valuesCompared = 0;
        foreach (var t in tables)
        {
            var sourceColumns = await SourceColumnsAsync(sql, t);
            var columns = t.Columns.Where(c => sourceColumns.Contains(c.Column)).ToList();
            var skipped = t.Columns.Where(c => !sourceColumns.Contains(c.Column)).Select(c => c.Column).ToList();
            if (skipped.Count > 0)
                Console.WriteLine($"  {t.Table}: source has no column(s) {string.Join(", ", skipped)}; they keep their defaults");

            var select = $"SELECT {string.Join(", ", columns.Select(c => $"[{c.Column}]"))} FROM [{t.Schema ?? "dbo"}].[{t.Table}]";
            var insert = $"INSERT INTO {Pg(t)} ({string.Join(", ", columns.Select(c => $"\"{c.Column}\""))}) " +
                         $"VALUES ({string.Join(", ", columns.Select((_, i) => $"@p{i}"))})";
            // Ids are copied as they are; a column that is always generated (none today) would need OVERRIDING
            if (t.IdentityColumn != null)
                insert = insert.Replace(") VALUES (", ") OVERRIDING SYSTEM VALUE VALUES (");

            long copied = 0;
            await using (var read = await new SqlCommand(select, sql).ExecuteReaderAsync())
            {
                while (await read.ReadAsync())
                {
                    await using var write = new NpgsqlCommand(insert, pg, tx);
                    for (var i = 0; i < columns.Count; i++)
                        write.Parameters.AddWithValue($"p{i}", ToPgValue(read.GetValue(i), columns[i].ClrType));
                    await write.ExecuteNonQueryAsync();
                    copied++;
                }
            }

            if (t.IdentityColumn != null)
            {
                // Next generated id continues after the copied ones
                await new NpgsqlCommand(
                    $"SELECT setval(pg_get_serial_sequence('{PgName(t)}', '{t.IdentityColumn}'), " +
                    $"COALESCE((SELECT MAX(\"{t.IdentityColumn}\") FROM {Pg(t)}), 0) + 1, false)", pg, tx).ExecuteScalarAsync();
            }

            var sourceCount = System.Convert.ToInt64(await new SqlCommand($"SELECT COUNT_BIG(*) FROM [{t.Schema ?? "dbo"}].[{t.Table}]", sql).ExecuteScalarAsync());
            summary.Add((t.Table, sourceCount, copied));

            // Read everything back and compare value by value before anything is committed
            var sourceRows = await ReadNormalizedAsync(new SqlCommand(select, sql));
            var targetRows = await ReadNormalizedAsync(new NpgsqlCommand(
                $"SELECT {string.Join(", ", columns.Select(c => $"\"{c.Column}\""))} FROM {Pg(t)}", pg, tx));
            var differences = sourceRows.Except(targetRows, StringComparer.Ordinal).Count()
                              + targetRows.Except(sourceRows, StringComparer.Ordinal).Count();
            if (differences > 0)
            {
                await tx.RollbackAsync();
                foreach (var row in sourceRows.Except(targetRows, StringComparer.Ordinal)) Console.Error.WriteLine($"  source: {row.Replace("\u001f", " | ")}");
                foreach (var row in targetRows.Except(sourceRows, StringComparer.Ordinal)) Console.Error.WriteLine($"  target: {row.Replace("\u001f", " | ")}");
                throw new InvalidOperationException($"{t.Table}: {differences} row(s) differ between source and target after the copy; nothing was committed.");
            }
            valuesCompared += (long)sourceRows.Count * columns.Count;
        }

        var mismatched = summary.Where(s => s.Source != s.Copied).ToList();
        if (mismatched.Count > 0)
        {
            await tx.RollbackAsync();
            throw new InvalidOperationException($"Row counts differ for {string.Join(", ", mismatched.Select(m => m.Table))}; nothing was committed.");
        }
        await tx.CommitAsync();

        Console.WriteLine();
        var width = summary.Max(s => s.Table.Length);
        foreach (var (table, sourceRows, copied) in summary)
            Console.WriteLine($"  {table.PadRight(width)}  {copied,6} row(s)");
        Console.WriteLine($"Copied {summary.Sum(s => s.Copied)} row(s) in {summary.Count} table(s); all {valuesCompared} values match the source.");
        Console.WriteLine("(Timestamps are kept to the microsecond, PostgreSQL's precision; SQL Server stored a seventh digit.)");
    }

    /// <summary>Rows as provider-neutral strings (same rendering for SQL Server and PostgreSQL values).</summary>
    private static async Task<List<string>> ReadNormalizedAsync(System.Data.Common.DbCommand command)
    {
        var rows = new List<string>();
        await using (command)
        await using (var read = await command.ExecuteReaderAsync())
        {
            while (await read.ReadAsync())
            {
                var values = new string[read.FieldCount];
                for (var i = 0; i < read.FieldCount; i++)
                    values[i] = read.GetValue(i) switch
                    {
                        DBNull => "\u0000null",
                        // PostgreSQL keeps timestamps to the microsecond; SQL Server datetime2 to 100 ns
                        DateTime d => ToMicroseconds(d).ToString("yyyy-MM-ddTHH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture),
                        DateTimeOffset o => ToMicroseconds(o.UtcDateTime).ToString("yyyy-MM-ddTHH:mm:ss.ffffff", System.Globalization.CultureInfo.InvariantCulture),
                        byte[] bytes => System.Convert.ToBase64String(bytes),
                        byte b => b.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                        var v => v.ToString() ?? ""
                    };
                rows.Add(string.Join("\u001f", values));
            }
        }
        return rows;
    }

    private static DateTime ToMicroseconds(DateTime value) => new(value.Ticks - value.Ticks % 10, value.Kind);

    private static object ToPgValue(object value, Type clrType)
    {
        if (value is DBNull) return DBNull.Value;
        var type = Nullable.GetUnderlyingType(clrType) ?? clrType;
        // EF maps byte / sbyte to smallint on PostgreSQL
        if (value is byte b && type != typeof(byte[])) return (short)b;
        if (value is sbyte sb) return (short)sb;
        return value;
    }

    private static async Task<HashSet<string>> SourceColumnsAsync(SqlConnection sql, TableMap t)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = new SqlCommand(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = @s AND TABLE_NAME = @t", sql);
        cmd.Parameters.AddWithValue("@s", t.Schema ?? "dbo");
        cmd.Parameters.AddWithValue("@t", t.Table);
        await using var read = await cmd.ExecuteReaderAsync();
        while (await read.ReadAsync()) columns.Add(read.GetString(0));
        if (columns.Count == 0)
            throw new InvalidOperationException($"The source database has no table {t.Table}. Is it a DeployVare database with all migrations applied?");
        return columns;
    }

    private static string Pg(TableMap t) => t.Schema == null ? $"\"{t.Table}\"" : $"\"{t.Schema}\".\"{t.Table}\"";
    private static string PgName(TableMap t) => Pg(t).Replace("'", "''");

    /// <summary>Every mapped table, ordered so a table comes after the tables its foreign keys point to.</summary>
    private static List<TableMap> OrderParentsFirst(IModel model)
    {
        var entities = model.GetEntityTypes().Where(e => e.GetTableName() != null && !e.IsOwned()).ToList();
        var ordered = new List<IEntityType>();
        var visiting = new HashSet<IEntityType>();
        var done = new HashSet<IEntityType>();

        void Visit(IEntityType entity)
        {
            if (done.Contains(entity)) return;
            if (!visiting.Add(entity)) return; // a cycle would be broken here; DeployVare has none
            foreach (var fk in entity.GetForeignKeys())
                if (fk.PrincipalEntityType != entity) Visit(fk.PrincipalEntityType);
            visiting.Remove(entity);
            done.Add(entity);
            ordered.Add(entity);
        }

        foreach (var entity in entities.OrderBy(e => e.GetTableName(), StringComparer.Ordinal)) Visit(entity);

        return ordered.Select(e =>
        {
            var table = e.GetTableName()!;
            var schema = e.GetSchema();
            var store = StoreObjectIdentifier.Table(table, schema);
            var columns = e.GetProperties()
                .Where(p => p.GetColumnName(store) != null)
                .Select(p => (p.GetColumnName(store)!, p.GetProviderClrType() ?? p.ClrType))
                .ToList();
            var identity = e.GetProperties().FirstOrDefault(p =>
                p.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.OnAdd &&
                p.IsPrimaryKey() && (p.ClrType == typeof(int) || p.ClrType == typeof(long)));
            return new TableMap(table, schema, columns, identity?.GetColumnName(store));
        }).ToList();
    }
}
