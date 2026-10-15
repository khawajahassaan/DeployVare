using System.ComponentModel.DataAnnotations;
using API.Models.Enums;

namespace API.DTOs.Requests;

/// <summary>What a Linux deployment puts on servers.</summary>
public enum LinuxDeploymentTarget
{
    /// <summary>Static frontend files served by Nginx/Apache.</summary>
    Frontend,
    /// <summary>The .NET API as a systemd service behind Nginx/Apache.</summary>
    Backend,
    /// <summary>API (which also reaches the database) and frontend.</summary>
    TwoTier,
    /// <summary>Database, API and frontend; the manual download has a separate database kit.</summary>
    ThreeTier
}

/// <summary>SSH login to a Linux server. Never stored; used only for the request.</summary>
public class LinuxSshTarget
{
    [Required, MaxLength(253)]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 22;

    [Required, MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Password { get; set; }

    /// <summary>OpenSSH/PEM private key text (unencrypted). Used instead of the password when set.</summary>
    [MaxLength(16000)]
    public string? PrivateKey { get; set; }

    /// <summary>Password for sudo when it differs from the login password (or with key login). Not needed for root.</summary>
    [MaxLength(500)]
    public string? SudoPassword { get; set; }
}

/// <summary>Builds and settings shared by manual packages and one-click deployments.</summary>
public class LinuxPackageRequest
{
    [Range(1, int.MaxValue)]
    public int ApplicationId { get; set; }

    public ArtifactEnvironment Environment { get; set; } = ArtifactEnvironment.Dev;

    public LinuxDeploymentTarget Target { get; set; } = LinuxDeploymentTarget.Backend;

    // Builds: null means the latest successful build; artifact null means the Linux artifact is chosen automatically
    public int? ApiBuildId { get; set; }
    [MaxLength(200)] public string? ApiArtifactName { get; set; }
    public int? FrontendBuildId { get; set; }
    [MaxLength(200)] public string? FrontendArtifactName { get; set; }

    /// <summary>Domain or IP the API site answers on. Empty: no API site (reach it through the frontend's API route).</summary>
    [MaxLength(253)] public string? ApiHostName { get; set; }
    /// <summary>Domain or IP the frontend site answers on (required with a frontend).</summary>
    [MaxLength(253)] public string? FrontendHostName { get; set; }

    [MaxLength(300)] public string? ApiDeploymentPath { get; set; }
    [MaxLength(300)] public string? FrontendDeploymentPath { get; set; }
    [MaxLength(100)] public string? ServiceName { get; set; }

    [Range(1024, 65535)]
    public int ApiPort { get; set; } = 5000;

    /// <summary>nginx or apache.</summary>
    [MaxLength(10)] public string WebServer { get; set; } = "nginx";

    // PEM files already on the server (e.g. /etc/letsencrypt/live/site/fullchain.pem). Empty: HTTP only.
    [MaxLength(300)] public string? ApiCertificatePath { get; set; }
    [MaxLength(300)] public string? ApiCertificateKeyPath { get; set; }
    [MaxLength(300)] public string? FrontendCertificatePath { get; set; }
    [MaxLength(300)] public string? FrontendCertificateKeyPath { get; set; }

    /// <summary>The frontend site forwards /api (ApiProxyPath) to the backend.</summary>
    public bool ProxyApiThroughFrontend { get; set; }
    [MaxLength(50)] public string? ApiProxyPath { get; set; }
    /// <summary>Where that route goes, e.g. http://127.0.0.1:5000 when both run on one server. Empty: worked out.</summary>
    [MaxLength(300)] public string? ApiProxyUpstream { get; set; }

    /// <summary>VITE_API_URL written to the frontend's config.js. Empty: the build's own config.js is kept.</summary>
    [MaxLength(500)] public string? FrontendApiUrl { get; set; }

    public bool InstallRedis { get; set; } = true;
    public bool RunMigrations { get; set; }

    // Connection string of a generated appsettings.json (the password is never part of a manual package)
    [MaxLength(253)] public string? DatabaseServer { get; set; }
    [MaxLength(128)] public string? DatabaseName { get; set; }
    [MaxLength(128)] public string? DatabaseUser { get; set; }

    /// <summary>The server reports its stages under this ID (GET apideployments/progress/{id}).</summary>
    [MaxLength(64)] public string? ProgressId { get; set; }
}

/// <summary>One-click deployment: the same settings plus the servers to deploy to.</summary>
public class LinuxDeployRequest : LinuxPackageRequest
{
    public LinuxSshTarget? ApiServer { get; set; }
    public LinuxSshTarget? FrontendServer { get; set; }

    /// <summary>
    /// SQL password for appsettings.json when the build has none and the API server has none yet. Written only to
    /// the server's appsettings.json; never logged or kept.
    /// </summary>
    [MaxLength(500)] public string? DatabasePassword { get; set; }
}
