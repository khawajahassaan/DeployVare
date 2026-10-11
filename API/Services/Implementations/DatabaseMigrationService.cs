using System.Security.Cryptography;
using System.Text.Json;
using API.Data;
using API.Models;
using API.Models.Entities;
using API.Services.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace API.Services.Implementations;

public class DatabaseMigrationService : IDatabaseMigrationService
{
    private const string OutputPrefix = "DV_MIGRATOR: ";
    private const string SuccessMarker = "DV_MIGRATION_SUCCESS";
    private const string FailureMarker = "DV_MIGRATION_FAILED:";
    private const string TargetCheckMarker = "DV_DBCHECK_JSON:";
    private const string JournalTable = "dbo.SchemaVersions";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly DeployVareDbContext _context;
    private readonly ISshService _sshService;
    private readonly IEncryptionService _encryptionService;
    private readonly ILogger<DatabaseMigrationService> _logger;

    public DatabaseMigrationService(
        DeployVareDbContext context,
        ISshService sshService,
        IEncryptionService encryptionService,
        ILogger<DatabaseMigrationService> logger)
    {
        _context = context;
        _sshService = sshService;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<(string DataSource, string DatabaseName)> ResolveTargetAsync(ApplicationServer server)
    {
        if (server.DatabaseInstanceId == null)
            throw new InvalidOperationException(
                "No database instance is linked to this server. Link it in Server Setup (Database Access) so migrations know which database to update.");

        var instance = await _context.DatabaseInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == server.DatabaseInstanceId.Value)
            ?? throw new InvalidOperationException("The database instance linked to this server no longer exists.");

        try
        {
            // Only the server and database names are used; migrations run with Windows authentication
            var builder = new SqlConnectionStringBuilder(_encryptionService.Decrypt(instance.ConnectionStringEncrypted));
            if (string.IsNullOrWhiteSpace(builder.DataSource) || string.IsNullOrWhiteSpace(builder.InitialCatalog))
                throw new InvalidOperationException($"The connection string of database instance '{instance.InstanceName}' has no server or database name.");

            return (builder.DataSource, builder.InitialCatalog);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"The connection string of database instance '{instance.InstanceName}' could not be read.", ex);
        }
    }

    public async Task<MigrationTargetCheck> CheckTargetAsync(ApplicationServer server, string dataSource, string databaseName)
    {
        _logger.LogInformation("MigrationTargetCheckStarted on {ServerName}: database {Database} on {DataSource}",
            server.ServerName, databaseName, dataSource);

        var script = CheckTargetScript
            .Replace("__DATASOURCE__", Ps(dataSource))
            .Replace("__DATABASE__", Ps(databaseName));

        var (password, privateKey) = GetCredentials(server);
        var (_, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, script);

        var json = output
            .Split('\n')
            .Select(l => l.Trim())
            .LastOrDefault(l => l.StartsWith(TargetCheckMarker, StringComparison.Ordinal))?[TargetCheckMarker.Length..];

        MigrationTargetCheck check;
        if (json == null)
        {
            // The check itself could not run on the server (SSH failure, PowerShell error)
            var detail = (error ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(l => !l.StartsWith("#< CLIXML", StringComparison.Ordinal)) ?? "no result was returned";

            check = new MigrationTargetCheck
            {
                Status = MigrationTargetStatus.ServerUnreachable,
                Outcome = MigrationTargetOutcome.Block,
                Message = $"The database check could not run on {server.ServerName}: {detail}"
            };
        }
        else
        {
            MigrationTargetFacts facts;
            try
            {
                facts = JsonSerializer.Deserialize<MigrationTargetFacts>(json, JsonOptions) ?? new MigrationTargetFacts();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Unexpected database check output from {ServerName}", server.ServerName);
                facts = new MigrationTargetFacts { ConnectionError = "The server returned an unreadable database check result." };
            }

            check = ClassifyTarget(facts, dataSource, databaseName);
        }

        _logger.LogInformation("MigrationTargetCheckCompleted on {ServerName}: {Status} ({Outcome}) {Message}",
            server.ServerName, check.Status, check.Outcome, check.Message);

        return check;
    }

    /// <summary>
    /// Decides whether DbMigrator may run. Connection problems are kept apart from a missing database: only a
    /// reachable server without the database (or with an empty one) is a safe skip.
    /// </summary>
    public static MigrationTargetCheck ClassifyTarget(MigrationTargetFacts facts, string dataSource, string databaseName)
    {
        MigrationTargetCheck Result(MigrationTargetStatus status, MigrationTargetOutcome outcome, string message) => new()
        {
            Status = status,
            Outcome = outcome,
            Message = message,
            UserTableCount = facts.UserTableCount,
            AppliedScriptCount = facts.AppliedScriptCount
        };

        if (!facts.Connected)
        {
            if (facts.SqlServerInstalled == false)
                return Result(MigrationTargetStatus.SqlServerNotInstalled, MigrationTargetOutcome.Block,
                    $"SQL Server is not installed on this server, so '{databaseName}' on '{dataSource}' cannot be reached. Install SQL Server or point the database instance at the right server.");

            var error = facts.ConnectionError ?? "unknown error";
            return error.Contains("Login failed", StringComparison.OrdinalIgnoreCase)
                ? Result(MigrationTargetStatus.LoginFailed, MigrationTargetOutcome.Block,
                    $"SQL Server '{dataSource}' rejected the Windows login used for migrations: {error}")
                : Result(MigrationTargetStatus.ServerUnreachable, MigrationTargetOutcome.Block,
                    $"Could not connect to SQL Server '{dataSource}': {error}");
        }

        if (!facts.DatabaseExists)
            return Result(MigrationTargetStatus.DatabaseMissing, MigrationTargetOutcome.Skip,
                $"Database '{databaseName}' does not exist on SQL Server '{dataSource}', so there is nothing to migrate yet.");

        if (!string.Equals(facts.DatabaseState, "ONLINE", StringComparison.OrdinalIgnoreCase))
            return Result(MigrationTargetStatus.DatabaseUnavailable, MigrationTargetOutcome.Block,
                $"Database '{databaseName}' exists on '{dataSource}' but is {facts.DatabaseState ?? "not online"}.");

        if (!facts.CanOpenDatabase)
            return Result(MigrationTargetStatus.AccessDenied, MigrationTargetOutcome.Block,
                $"Database '{databaseName}' exists but {facts.Login ?? "the migration login"} cannot open it: {facts.OpenError ?? "access denied"}");

        if (facts.JournalTableExists)
            return Result(MigrationTargetStatus.Ready, MigrationTargetOutcome.Run,
                $"Database '{databaseName}' is ready for migrations ({facts.AppliedScriptCount} script(s) already applied, {facts.UserTableCount} table(s))");

        if (facts.UserTableCount == 0)
            return Result(MigrationTargetStatus.EmptyDatabase, MigrationTargetOutcome.Skip,
                $"Database '{databaseName}' exists but has no tables. DbMigrator only applies incremental scripts to an existing application database, so an empty database is not migrated.");

        return Result(MigrationTargetStatus.UnjournaledDatabase, MigrationTargetOutcome.Block,
            $"Database '{databaseName}' has {facts.UserTableCount} table(s) but no {JournalTable} journal, so DbMigrator would re-run every one-time script from the beginning. Baseline the journal or migrate this database manually first.");
    }

    public async Task<DatabaseMigrationResult> RunDbMigratorAsync(ApplicationServer server, string dataSource, string databaseName,
        string stagedPackagePath, string stagedScriptsPath)
    {
        _logger.LogInformation("DatabaseMigrationStarted on {ServerName}: database {Database} on {DataSource}",
            server.ServerName, databaseName, dataSource);

        var script = RunDbMigratorScript
            .Replace("__PACKAGE__", Ps(stagedPackagePath))
            .Replace("__SCRIPTS__", Ps(stagedScriptsPath))
            .Replace("__DATASOURCE__", Ps(dataSource))
            .Replace("__DATABASE__", Ps(databaseName));

        var (password, privateKey) = GetCredentials(server);

        var (_, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, script);

        var result = ParseOutput(output, error);

        _logger.LogInformation("DbMigrator output for {ServerName} ({Database}):{NewLine}{Output}",
            server.ServerName, databaseName, Environment.NewLine,
            string.Join(Environment.NewLine, ExtractMigratorLines(output)));

        if (result.Success)
            _logger.LogInformation("DatabaseMigrationSucceeded on {ServerName}: {Count} script(s) executed", server.ServerName, result.ScriptsExecuted);
        else
            _logger.LogWarning("DatabaseMigrationFailed on {ServerName}: {Message}", server.ServerName, result.Message);

        return result;
    }

    /// <summary>Interprets the output of <see cref="RunDbMigratorScript"/>.</summary>
    public static DatabaseMigrationResult ParseOutput(string output, string? error = null)
    {
        var lines = (output ?? string.Empty).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var migratorLines = ExtractMigratorLines(output ?? string.Empty);

        var executed = migratorLines.Count(l => l.Contains("Executing Database Server script", StringComparison.OrdinalIgnoreCase));
        var succeeded = lines.Any(l => l.Trim() == SuccessMarker);
        var failure = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(FailureMarker, StringComparison.Ordinal))?[FailureMarker.Length..].Trim();

        if (succeeded && failure == null)
        {
            return new DatabaseMigrationResult
            {
                Success = true,
                Message = $"Database migrations applied ({executed} script(s) executed)",
                ScriptsExecuted = executed
            };
        }

        var errorLine = (error ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => !l.StartsWith("#< CLIXML", StringComparison.Ordinal));

        return new DatabaseMigrationResult
        {
            Success = false,
            Message = failure ?? errorLine ?? "DbMigrator did not report a result",
            ScriptsExecuted = executed,
            OutputTail = migratorLines.Where(l => l.Trim().Length > 0).TakeLast(15).ToList()
        };
    }

    private static List<string> ExtractMigratorLines(string output) => output
        .Split('\n')
        .Select(l => l.TrimEnd('\r'))
        .Where(l => l.StartsWith(OutputPrefix, StringComparison.Ordinal))
        .Select(l => l[OutputPrefix.Length..])
        .ToList();

    private (string? Password, string? PrivateKey) GetCredentials(ApplicationServer server)
    {
        var password = string.IsNullOrEmpty(server.EncryptedPassword) ? null : _encryptionService.Decrypt(server.EncryptedPassword);
        var privateKey = string.IsNullOrEmpty(server.SshPrivateKey) ? null : server.SshPrivateKey;
        return (password, privateKey);
    }

    private static string Ps(string value) => value.Replace("'", "''");

    // Uses the same Windows authentication as DbMigrator. Server-level problems (not installed, unreachable, login)
    // are reported separately from the database being absent, empty or not managed by DbUp.
    private const string CheckTargetScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        $dataSource = '__DATASOURCE__'
        $database = '__DATABASE__'
        $result = [ordered]@{
            sqlServerInstalled = $null; connected = $false; connectionError = $null; login = $null; machineName = $null
            databaseExists = $false; databaseState = $null; canOpenDatabase = $false; openError = $null
            userTableCount = 0; journalTableExists = $false; appliedScriptCount = 0
        }

        function New-DvCheckConnection([string]$catalog) {
            $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
            $builder['Data Source'] = $dataSource
            $builder['Initial Catalog'] = $catalog
            $builder['Integrated Security'] = $true
            $builder['Connect Timeout'] = 15
            $builder['TrustServerCertificate'] = $true
            $builder['Application Name'] = 'DeployVare pre-migration check'
            $connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
            $connection.Open()
            return $connection
        }

        # For a local data source, tell "SQL Server is not installed" apart from "cannot connect"
        $sqlHost = (($dataSource -replace '^(tcp|np|lpc|admin):', '') -split '[\\,]')[0].Trim()
        if (@('.', '(local)', 'localhost', '127.0.0.1', '::1', $env:COMPUTERNAME) -contains $sqlHost) {
            $result.sqlServerInstalled = $null -ne (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction SilentlyContinue)
        }

        # 1. Server level: connect to master and look the database up (never opens the application database)
        try {
            $master = New-DvCheckConnection 'master'
            try {
                $command = $master.CreateCommand()
                $command.CommandText = "SELECT SUSER_SNAME(), CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)), (SELECT state_desc FROM sys.databases WHERE name = @database)"
                $null = $command.Parameters.AddWithValue('@database', $database)
                $reader = $command.ExecuteReader()
                if ($reader.Read()) {
                    $result.login = [string]$reader.GetValue(0)
                    $result.machineName = [string]$reader.GetValue(1)
                    if (-not $reader.IsDBNull(2)) {
                        $result.databaseExists = $true
                        $result.databaseState = [string]$reader.GetValue(2)
                    }
                }
                $reader.Close()
                $result.connected = $true
            }
            finally {
                $master.Close()
            }
        }
        catch {
            $exception = $_.Exception
            if ($exception.InnerException) { $exception = $exception.InnerException }
            $result.connectionError = $exception.Message
        }

        # 2. Database level: only for an existing, online database - is it an application database DbUp manages?
        if ($result.connected -and $result.databaseExists -and $result.databaseState -eq 'ONLINE') {
            try {
                $db = New-DvCheckConnection $database
                try {
                    $command = $db.CreateCommand()
                    $command.CommandText = @'
        SELECT
            (SELECT COUNT(*) FROM sys.tables
             WHERE is_ms_shipped = 0 AND NOT (name = N'SchemaVersions' AND SCHEMA_NAME(schema_id) = N'dbo')) AS UserTableCount,
            CASE WHEN OBJECT_ID(N'dbo.SchemaVersions', N'U') IS NULL THEN 0 ELSE 1 END AS JournalTableExists
        '@
                    $reader = $command.ExecuteReader()
                    if ($reader.Read()) {
                        $result.userTableCount = [int]$reader.GetValue(0)
                        $result.journalTableExists = [int]$reader.GetValue(1) -eq 1
                    }
                    $reader.Close()

                    if ($result.journalTableExists) {
                        $command = $db.CreateCommand()
                        $command.CommandText = 'SELECT COUNT(*) FROM dbo.SchemaVersions'
                        $result.appliedScriptCount = [int]$command.ExecuteScalar()
                    }

                    $result.canOpenDatabase = $true
                }
                finally {
                    $db.Close()
                }
            }
            catch {
                $exception = $_.Exception
                if ($exception.InnerException) { $exception = $exception.InnerException }
                $result.openError = $exception.Message
            }
        }

        Write-Output ('DV_DBCHECK_JSON:' + (ConvertTo-Json -InputObject $result -Compress))
        exit 0
        """;

    // Mirrors the pipeline: find DbMigrator.exe, build a Windows-auth connection string, pass the scripts folder,
    // and treat any non-zero exit code as a failure. Every output line is prefixed so it can be separated from noise.
    private const string RunDbMigratorScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        try {
            $packagePath = '__PACKAGE__'
            $scriptsPath = '__SCRIPTS__'

            $exe = Get-ChildItem -LiteralPath $packagePath -Filter 'DbMigrator.exe' -Recurse -File -ErrorAction SilentlyContinue |
                Select-Object -First 1 -ExpandProperty FullName
            if (-not $exe) { throw 'DbMigrator.exe was not found in the deployment package' }

            $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
            $builder['Data Source'] = '__DATASOURCE__'
            $builder['Initial Catalog'] = '__DATABASE__'
            $builder['Integrated Security'] = $true
            $builder['MultipleActiveResultSets'] = $true
            $builder['TrustServerCertificate'] = $true

            $ErrorActionPreference = 'Continue'
            $output = & $exe $builder.ConnectionString $scriptsPath 2>&1
            $exitCode = $LASTEXITCODE
            foreach ($line in $output) { Write-Output ('DV_MIGRATOR: ' + [string]$line) }

            if ($exitCode -ne 0) {
                Write-Output "DV_MIGRATION_FAILED: DbMigrator exited with code $exitCode"
                exit 1
            }

            Write-Output 'DV_MIGRATION_SUCCESS'
            exit 0
        }
        catch {
            Write-Output "DV_MIGRATION_FAILED: $($_.Exception.Message)"
            exit 1
        }
        """;
}
