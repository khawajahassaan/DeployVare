namespace API.Models;

/// <summary>
/// What the target server reported about the migration database, collected with the same Windows
/// authentication DbMigrator uses (deserialized from the pre-migration check script).
/// </summary>
public class MigrationTargetFacts
{
    /// <summary>False when the data source is on this server and no SQL Server instance is installed; null when not determined.</summary>
    public bool? SqlServerInstalled { get; set; }

    public bool Connected { get; set; }
    public string? ConnectionError { get; set; }
    public string? Login { get; set; }
    public string? MachineName { get; set; }

    public bool DatabaseExists { get; set; }

    /// <summary>sys.databases state_desc, e.g. ONLINE, OFFLINE, RESTORING.</summary>
    public string? DatabaseState { get; set; }

    public bool CanOpenDatabase { get; set; }
    public string? OpenError { get; set; }

    /// <summary>User tables, not counting DbUp's journal table.</summary>
    public int UserTableCount { get; set; }

    /// <summary>dbo.SchemaVersions exists (DbMigrator's DbUp journal).</summary>
    public bool JournalTableExists { get; set; }

    public int AppliedScriptCount { get; set; }
}
