namespace API.Models;

/// <summary>
/// Written into deploy-config.json inside a manual-deployment package. No passwords.
/// Deploy.ps1 reads this file from the package root.
/// </summary>
public class ManualDeployConfig
{
    public string ServerName { get; set; } = string.Empty;
    public string ServerType { get; set; } = "Api";
    public string WebsiteName { get; set; } = string.Empty;
    public string AppPoolName { get; set; } = string.Empty;
    public string DeploymentPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = @"C:\DeployVare\Backups";
    public string SiteHostName { get; set; } = string.Empty;
    public bool RequireSni { get; set; }
    public bool PreserveWebConfig { get; set; } = true;
    public List<string> KeepOnServerPaths { get; set; } = new();
    public bool FreshDatabase { get; set; }
    public string SqlInstanceName { get; set; } = "SQLEXPRESS";
    public string SqlDataSource { get; set; } = @".\SQLEXPRESS";
    public string? DatabaseName { get; set; }
    public string CertificateMode { get; set; } = "Default";
    public string? CertificateFile { get; set; }
    public string DefaultCertificateName { get; set; } = "WMSVC-SHA2";
    public string LicensingMode { get; set; } = "WithoutLicense";
    public string? LicenseFileName { get; set; }
    public bool HasScripts { get; set; }
    public List<string> ContentFolders { get; set; } = new();
    public int RequiredDotNetMajor { get; set; } = 10;
    public int MinimumSdkMajor { get; set; } = 8;
    public int RequiredRedisMajor { get; set; } = 8;
    public int RedisPort { get; set; } = 6379;
}
