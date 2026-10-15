namespace API.Models;

/// <summary>
/// deploy-config.json of a Linux kit, read by DeploymentScripts/Deploy.sh. One kit per server role:
/// Api (Kestrel behind Nginx/Apache as a systemd service), Frontend (static files served by Nginx/Apache)
/// or Database (DbMigrator and the SQL scripts, run against an existing SQL Server database).
/// </summary>
public class LinuxDeployConfig
{
    public string KitType { get; set; } = "Api";
    public string ApplicationName { get; set; } = string.Empty;
    public string BuildNumber { get; set; } = string.Empty;
    public string? PackageSetId { get; set; }

    /// <summary>Name of the Nginx/Apache site file, e.g. auditvare-api.</summary>
    public string SiteName { get; set; } = string.Empty;

    /// <summary>server_name / ServerName (domain or IP). Empty on an API kit: no site is created and the API is
    /// reached through the frontend's /api route.</summary>
    public string? SiteHostName { get; set; }

    public string DeploymentPath { get; set; } = string.Empty;
    public string WebServer { get; set; } = "nginx";

    /// <summary>PEM certificate and key already on the server (e.g. from certbot). Both empty: HTTP only.</summary>
    public string? CertificatePath { get; set; }
    public string? CertificateKeyPath { get; set; }

    // API kit
    public string? ServiceName { get; set; }
    public string ServiceUser { get; set; } = "www-data";
    /// <summary>File started by systemd: App.dll (run with dotnet) or App (self-contained linux-x64 binary).</summary>
    public string? AppEntry { get; set; }
    public bool SelfContained { get; set; }
    public int AppPort { get; set; } = 5000;
    public int RequiredDotNetMajor { get; set; } = 10;
    public bool InstallRedis { get; set; }
    public int RedisPort { get; set; } = 6379;
    public bool AppSettingsGenerated { get; set; }

    /// <summary>Relative paths kept when they already exist on the server (appsettings.json is always kept when generated).</summary>
    public List<string> KeepOnServerPaths { get; set; } = new();

    // Frontend kit: optional /api route to the backend (e.g. http://127.0.0.1:5000 when both run on one server)
    public string? ApiProxyPath { get; set; }
    public string? ApiProxyUpstream { get; set; }

    // Database (and API kits that migrate)
    public bool RunMigrations { get; set; }
    public bool HasScripts { get; set; }
    public string? DbMigratorFolder { get; set; }

    public string BackupPath { get; set; } = "/var/backups/deployvare";
}
