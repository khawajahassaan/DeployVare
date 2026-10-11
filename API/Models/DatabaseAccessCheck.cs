namespace API.Models;

/// <summary>
/// Database access for a server's app pool identity: the linked database instance, the SQL login that
/// should have access, and what the server reported.
/// </summary>
public class DatabaseAccessCheck
{
    public int? DatabaseInstanceId { get; set; }
    public string? InstanceName { get; set; }
    public string? DataSource { get; set; }
    public string? DatabaseName { get; set; }

    /// <summary>Windows login granted access, e.g. IIS APPPOOL\AuditVareDevBackend.</summary>
    public string LoginName { get; set; } = string.Empty;

    /// <summary>Set when the check could not run (unreadable connection string, unknown identity).</summary>
    public string? ConfigurationError { get; set; }

    public DatabaseFacts? Facts { get; set; }
}

/// <summary>Raw SQL Server facts collected over SSH with Windows authentication.</summary>
public class DatabaseFacts
{
    public bool SqlOnSameServer { get; set; }
    public string? SqlHost { get; set; }
    public bool Connected { get; set; }
    public string? ConnectionError { get; set; }
    public string? ConnectedAs { get; set; }
    public bool IsSysadmin { get; set; }
    public bool DatabaseExists { get; set; }
    public bool LoginExists { get; set; }
    public bool UserExists { get; set; }
    public bool IsDbOwner { get; set; }
}
