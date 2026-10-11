namespace API.DTOs.Responses;

public class SqlServerInfoDto
{
    public bool Installed { get; set; }
    public List<SqlServerInstanceDto> Instances { get; set; } = new();
}

public class SqlDatabaseUserDto
{
    public string UserName { get; set; } = string.Empty;
    public string UserType { get; set; } = string.Empty;

    /// <summary>Server login mapped to the user; null for orphaned users.</summary>
    public string? LoginName { get; set; }

    public bool LoginDisabled { get; set; }
    public bool LoginLocked { get; set; }
    public List<string> Roles { get; set; } = new();
}

public class SqlServerInstanceDto
{
    /// <summary>Instance name; MSSQLSERVER for the default instance.</summary>
    public string Name { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string DataSource { get; set; } = string.Empty;
    public bool Running { get; set; }
    public string? Version { get; set; }
    public string? Edition { get; set; }

    /// <summary>True when SQL Server accepts SQL logins (SQL Server and Windows authentication mode).</summary>
    public bool MixedAuthentication { get; set; }

    public bool Connected { get; set; }
    public string? ConnectionError { get; set; }
    public string? ConnectedAs { get; set; }

    /// <summary>The connected login can create logins (sysadmin or securityadmin).</summary>
    public bool CanManageLogins { get; set; }
    public List<string> Databases { get; set; } = new();
}
