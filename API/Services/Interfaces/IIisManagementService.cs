using API.Models.Entities;

namespace API.Services.Interfaces;

public interface IIisManagementService
{
    /// <summary>
    /// Stops IIS Application Pool
    /// </summary>
    Task<(bool Success, string Message)> StopAppPoolAsync(ApplicationServer server);

    /// <summary>
    /// Starts IIS Application Pool
    /// </summary>
    Task<(bool Success, string Message)> StartAppPoolAsync(ApplicationServer server);

    /// <summary>
    /// Stops IIS Website
    /// </summary>
    Task<(bool Success, string Message)> StopWebsiteAsync(ApplicationServer server);

    /// <summary>
    /// Starts IIS Website
    /// </summary>
    Task<(bool Success, string Message)> StartWebsiteAsync(ApplicationServer server);

    /// <summary>
    /// Creates a backup of deployment files
    /// </summary>
    Task<(bool Success, string? BackupFileName, string Message)> CreateBackupAsync(ApplicationServer server);

    /// <summary>
    /// Verifies IIS services are running after deployment
    /// </summary>
    Task<(bool Success, string Message)> VerifyDeploymentAsync(ApplicationServer server);

    /// <summary>
    /// Gets the status of App Pool and Website
    /// </summary>
    Task<(bool AppPoolRunning, bool WebsiteRunning, string Message)> GetServiceStatusAsync(ApplicationServer server);
}





