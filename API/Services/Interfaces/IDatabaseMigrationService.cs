using API.Models.Entities;

namespace API.Services.Interfaces;

public sealed class DatabaseMigrationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ScriptsExecuted { get; set; }

    /// <summary>Last lines of DbMigrator output, shown when a migration fails.</summary>
    public List<string> OutputTail { get; set; } = new();
}

public enum MigrationTargetOutcome
{
    /// <summary>Existing DbUp-managed application database: run DbMigrator.</summary>
    Run,

    /// <summary>Nothing to migrate yet (database missing or empty): skip migrations and continue deploying.</summary>
    Skip,

    /// <summary>Server, access or schema problem: stop before any change to the site.</summary>
    Block
}

public enum MigrationTargetStatus
{
    Ready,
    DatabaseMissing,
    EmptyDatabase,
    UnjournaledDatabase,
    SqlServerNotInstalled,
    ServerUnreachable,
    LoginFailed,
    DatabaseUnavailable,
    AccessDenied
}

public sealed class MigrationTargetCheck
{
    public MigrationTargetStatus Status { get; init; }
    public MigrationTargetOutcome Outcome { get; init; }
    public string Message { get; init; } = string.Empty;
    public int UserTableCount { get; init; }
    public int AppliedScriptCount { get; init; }
}

/// <summary>
/// Runs the backend's DbMigrator (DbUp) on the target server, the same way the Azure DevOps pipeline's
/// "Migrate Database" stage does: DbMigrator.exe "&lt;connection string&gt;" &lt;SQL scripts folder&gt;.
/// </summary>
public interface IDatabaseMigrationService
{
    /// <summary>SQL Server and database to migrate, from the server's linked database instance.</summary>
    Task<(string DataSource, string DatabaseName)> ResolveTargetAsync(ApplicationServer server);

    /// <summary>
    /// Safety check before DbMigrator connects: can the SQL Server be reached, does the database exist, and is it
    /// an application database DbMigrator manages (DbUp journal present)?
    /// </summary>
    Task<MigrationTargetCheck> CheckTargetAsync(ApplicationServer server, string dataSource, string databaseName);

    /// <summary>
    /// Runs DbMigrator.exe found in the staged package against the database with Windows authentication as the
    /// SSH user (no password on the command line).
    /// </summary>
    Task<DatabaseMigrationResult> RunDbMigratorAsync(ApplicationServer server, string dataSource, string databaseName,
        string stagedPackagePath, string stagedScriptsPath);
}
