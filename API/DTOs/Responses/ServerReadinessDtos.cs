namespace API.DTOs.Responses;

public static class ReadinessStatus
{
    public const string Pass = "Pass";
    public const string Fail = "Fail";
    public const string Warning = "Warning";
    public const string Skipped = "Skipped";
}

/// <summary>Remediation the Server Setup page can offer for a failed item.</summary>
public static class ReadinessFix
{
    public const string InstallIis = "InstallIis";
    public const string CreateSite = "CreateSite";
    public const string InstallComponent = "InstallComponent";
    public const string ConfigureCertificate = "ConfigureCertificate";
    public const string ConfigureBindings = "ConfigureBindings";
    public const string UpdateDeploymentPath = "UpdateDeploymentPath";
    public const string ConfigureDatabase = "ConfigureDatabase";
    public const string GrantDatabaseAccess = "GrantDatabaseAccess";
    public const string ConfigureRuntimeSettings = "ConfigureRuntimeSettings";
}

public class ReadinessItemDto
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = ReadinessStatus.Fail;
    public string Detail { get; set; } = string.Empty;
    public bool Required { get; set; }

    /// <summary>One of <see cref="ReadinessFix"/>, or null when nothing can be fixed from DeployVare.</summary>
    public string? Fix { get; set; }

    /// <summary>For InstallComponent fixes: the component to install.</summary>
    public string? Component { get; set; }

    /// <summary>For InstallComponent fixes: whether the installer file is available in DeployVare.</summary>
    public bool? InstallerAvailable { get; set; }
}

public class ServerCertificateDto
{
    public string Thumbprint { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? FriendlyName { get; set; }
    public DateTime? NotAfter { get; set; }
    public bool IsExpired { get; set; }
}

public class SiteBindingDto
{
    public string Protocol { get; set; } = string.Empty;
    public string BindingInformation { get; set; } = string.Empty;
    public string? HostName { get; set; }
    public int? Port { get; set; }
    public string? CertificateThumbprint { get; set; }
}

public class ServerReadinessReportDto
{
    public int ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; }
    public string? CheckedBy { get; set; }

    /// <summary>True when every required item passes.</summary>
    public bool IsReady { get; set; }

    public bool RebootPending { get; set; }
    public string? OperatingSystem { get; set; }
    public string? SitePhysicalPath { get; set; }
    public List<ReadinessItemDto> Items { get; set; } = new();
    public List<ServerCertificateDto> Certificates { get; set; } = new();
    public List<SiteBindingDto> Bindings { get; set; } = new();

    /// <summary>Database access details (API servers only).</summary>
    public DatabaseAccessDto? Database { get; set; }
}

public class DatabaseAccessDto
{
    public int? DatabaseInstanceId { get; set; }
    public string? InstanceName { get; set; }
    public string? DataSource { get; set; }
    public string? DatabaseName { get; set; }
    public string LoginName { get; set; } = string.Empty;
    public string? ConfigurationError { get; set; }
    public bool? SqlOnSameServer { get; set; }
    public bool? Connected { get; set; }
    public string? ConnectionError { get; set; }
    public string? ConnectedAs { get; set; }
    public bool? IsSysadmin { get; set; }
    public bool? DatabaseExists { get; set; }
    public bool? LoginExists { get; set; }
    public bool? UserExists { get; set; }
    public bool? IsDbOwner { get; set; }
}

public class InstallerFileDto
{
    public string Component { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? Version { get; set; }
    public long? SizeBytes { get; set; }
    public bool Available { get; set; }
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Installers folder DeployVare reads from.</summary>
    public string Directory { get; set; } = string.Empty;
}

public class ServerSetupActionResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool RebootRequired { get; set; }
    public List<string> Steps { get; set; } = new();
}
