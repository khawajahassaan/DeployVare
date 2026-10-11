using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Enums;

namespace API.Services.Interfaces;

/// <summary>
/// Verifies and prepares Windows servers for deployment (IIS, site, runtimes, Redis, SSL, bindings)
/// over the existing SSH connection.
/// </summary>
public interface IServerReadinessService
{
    /// <summary>Runs the readiness checklist on the server and saves it as the server's latest report.</summary>
    Task<ServerReadinessReportDto> CheckAsync(int serverId, string checkedBy = "system");

    /// <summary>Latest saved readiness report, or null when the server has never been checked.</summary>
    Task<ServerReadinessReportDto?> GetLastReportAsync(int serverId);

    List<InstallerFileDto> GetInstallers();

    Task<ServerSetupActionResultDto> InstallIisAsync(int serverId);

    Task<ServerSetupActionResultDto> InstallComponentAsync(int serverId, ServerComponent component);

    Task<ServerSetupActionResultDto> CreateSiteAsync(int serverId, CreateSiteRequest request);

    Task<ServerSetupActionResultDto> InstallCertificateAsync(int serverId, IFormFile certificateFile, string? password);

    Task<ServerSetupActionResultDto> ConfigureBindingsAsync(int serverId, ConfigureBindingsRequest request);

    /// <summary>Sets the server's DeployVare deployment path to the IIS site's physical folder.</summary>
    Task<ServerSetupActionResultDto> SyncDeploymentPathAsync(int serverId);

    /// <summary>Links (or unlinks, with null) the database instance the server's API uses.</summary>
    Task<ServerSetupActionResultDto> LinkDatabaseInstanceAsync(int serverId, int? databaseInstanceId);

    /// <summary>Creates the SQL login and database user for the app pool identity and adds it to db_owner.</summary>
    Task<ServerSetupActionResultDto> GrantDatabaseAccessAsync(int serverId);
}
