namespace API.Services.Interfaces;

/// <summary>
/// Linux server management: package installation, service management, system configuration.
/// Executes via SSH on remote Ubuntu/Debian servers.
/// </summary>
public interface ILinuxServerService
{
    /// <summary>Check if .NET SDK/Runtime is installed and meets minimum version.</summary>
    Task<(bool Installed, string? Version, string? Error)> CheckDotNetAsync(string host, int port, string username,
        string? password, string? privateKey, int minimumMajor);

    /// <summary>Install .NET runtime from Microsoft repositories.</summary>
    Task<(bool Success, string Output)> InstallDotNetAsync(string host, int port, string username,
        string? password, string? privateKey, int majorVersion, int timeoutMinutes = 15);

    /// <summary>Check if Redis/Valkey is running on the specified port.</summary>
    Task<(bool Running, string? Version, int? Port, string? Error)> CheckRedisAsync(string host, int port,
        string username, string? password, string? privateKey, int redisPort);

    /// <summary>Install Redis server from Ubuntu repositories.</summary>
    Task<(bool Success, string Output)> InstallRedisAsync(string host, int port, string username,
        string? password, string? privateKey, int redisPort, int timeoutMinutes = 10);

    /// <summary>Check if Nginx is installed and running.</summary>
    Task<(bool Installed, bool Running, string? Error)> CheckNginxAsync(string host, int port,
        string username, string? password, string? privateKey);

    /// <summary>Install and start Nginx.</summary>
    Task<(bool Success, string Output)> InstallNginxAsync(string host, int port, string username,
        string? password, string? privateKey, int timeoutMinutes = 10);

    /// <summary>Check if Apache2 is installed and running.</summary>
    Task<(bool Installed, bool Running, string? Error)> CheckApacheAsync(string host, int port,
        string username, string? password, string? privateKey);

    /// <summary>Install and start Apache2 with required modules.</summary>
    Task<(bool Success, string Output)> InstallApacheAsync(string host, int port, string username,
        string? password, string? privateKey, int timeoutMinutes = 10);

    /// <summary>Upload and install SSL certificate files.</summary>
    Task<(bool Success, string Error)> InstallCertificateAsync(string host, int port, string username,
        string? password, string? privateKey, string certPem, string keyPem);

    /// <summary>Create or update Nginx site configuration.</summary>
    Task<(bool Success, string Error)> ConfigureNginxSiteAsync(string host, int port, string username,
        string? password, string? privateKey, string siteName, string siteHostName,
        string deploymentPath, bool useSsl = true);

    /// <summary>Create or update Apache site configuration.</summary>
    Task<(bool Success, string Error)> ConfigureApacheSiteAsync(string host, int port, string username,
        string? password, string? privateKey, string siteName, string siteHostName,
        string deploymentPath, bool useSsl = true);

    /// <summary>Create systemd service file for .NET application.</summary>
    Task<(bool Success, string Error)> CreateSystemdServiceAsync(string host, int port, string username,
        string? password, string? privateKey, string serviceName, string appPath, string? environmentFile = null);

    /// <summary>Start or restart a systemd service.</summary>
    Task<(bool Success, string Error)> StartServiceAsync(string host, int port, string username,
        string? password, string? privateKey, string serviceName);

    /// <summary>Check systemd service status.</summary>
    Task<(bool Running, string? Status, string? Error)> GetServiceStatusAsync(string host, int port,
        string username, string? password, string? privateKey, string serviceName);

    /// <summary>Get current system facts (OS, hostname, RAM, disk space, etc.).</summary>
    Task<LinuxSystemFacts?> GetSystemFactsAsync(string host, int port, string username,
        string? password, string? privateKey);
}

/// <summary>Linux system information snapshot.</summary>
public class LinuxSystemFacts
{
    public string? Os { get; set; }
    public string? OsVersion { get; set; }
    public string? HostName { get; set; }
    public int? CpuCount { get; set; }
    public long? MemoryMb { get; set; }
    public long? DiskFreeMb { get; set; }
    public long? DiskTotalMb { get; set; }
    public string? KernelVersion { get; set; }
    public Dictionary<string, bool> InstalledPackages { get; set; } = new();
    public Dictionary<string, bool> RunningServices { get; set; } = new();
}
