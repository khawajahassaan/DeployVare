using System.ComponentModel.DataAnnotations;
using API.Models.Enums;

namespace API.DTOs.Requests;

public class InstallComponentRequest
{
    [Required]
    public ServerComponent Component { get; set; }
}

public class CreateSiteRequest
{
    /// <summary>Physical folder for the IIS site, e.g. C:\inetpub\wwwroot\AuditVareDevBackend.</summary>
    [Required]
    [MaxLength(500)]
    public string PhysicalPath { get; set; } = string.Empty;

    /// <summary>Site URL host name for the initial http binding, e.g. dev-api.auditvare.cloud.</summary>
    [Required]
    [MaxLength(253)]
    public string HostName { get; set; } = string.Empty;

    /// <summary>Also use this folder as the server's deployment path in DeployVare.</summary>
    public bool UseAsDeploymentPath { get; set; } = true;
}

public class ConfigureBindingsRequest
{
    /// <summary>Site URL host name, e.g. dev-api.auditvare.cloud.</summary>
    [Required]
    [MaxLength(253)]
    public string HostName { get; set; } = string.Empty;

    /// <summary>Thumbprint of a certificate already in LocalMachine\My for the 443 binding.</summary>
    [Required]
    [MaxLength(64)]
    public string CertificateThumbprint { get; set; } = string.Empty;

    public bool RequireSni { get; set; }
}

public class LinkDatabaseInstanceRequest
{
    /// <summary>Database instance of the server's application; null removes the link.</summary>
    public int? DatabaseInstanceId { get; set; }
}

/// <summary>How to connect to SQL Server. Credentials are never stored.</summary>
public class SqlServerConnectRequest
{
    /// <summary>Connect with Windows authentication as the SSH user instead of a SQL admin login.</summary>
    public bool UseWindowsAuthentication { get; set; }

    /// <summary>Existing SQL admin login (e.g. sa) used to connect.</summary>
    [MaxLength(128)]
    public string? AdminLogin { get; set; }

    [MaxLength(128)]
    public string? AdminPassword { get; set; }
}

public class SqlDatabaseUsersRequest : SqlServerConnectRequest
{
    [Required]
    [MaxLength(16)]
    public string InstanceName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string DatabaseName { get; set; } = string.Empty;
}

public class CreateSqlLoginRequest : SqlServerConnectRequest
{
    /// <summary>SQL Server instance on the server (MSSQLSERVER for the default instance).</summary>
    [Required]
    [MaxLength(16)]
    public string InstanceName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string DatabaseName { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string LoginName { get; set; } = string.Empty;

    /// <summary>Never stored by DeployVare.</summary>
    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string Password { get; set; } = string.Empty;

    /// <summary>When the login already exists, set this password and unlock it.</summary>
    public bool ResetPasswordIfExists { get; set; }
}

public class EnableSqlAuthenticationRequest
{
    [Required]
    [MaxLength(16)]
    public string InstanceName { get; set; } = string.Empty;
}

/// <summary>Installs SQL Server Express (mixed mode, TCP on 1433) from the Installers folder.</summary>
public class InstallSqlServerRequest
{
    /// <summary>Instance name; SQLEXPRESS is the Express default, MSSQLSERVER is the default instance.</summary>
    [MaxLength(16)]
    public string InstanceName { get; set; } = "SQLEXPRESS";

    /// <summary>
    /// Password for the sa login (mixed mode). Never stored by DeployVare. Must meet SQL Server's own
    /// complexity rules; no spaces or double-quotes so it is safe to pass to the setup program.
    /// </summary>
    [Required]
    [MinLength(8)]
    [MaxLength(128)]
    public string SaPassword { get; set; } = string.Empty;
}

public class SaveFrontendRuntimeConfigRequest
{
    /// <summary>Settings to save; entries with an empty value are dropped (the build's value is used).</summary>
    public List<DTOs.Responses.RuntimeSettingDto> Settings { get; set; } = new();
}

public class InstallCertificateRequest
{
    [Required]
    public IFormFile CertificateFile { get; set; } = null!;

    [MaxLength(200)]
    public string? Password { get; set; }
}
