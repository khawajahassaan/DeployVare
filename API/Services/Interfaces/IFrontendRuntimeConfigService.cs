using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Entities;

namespace API.Services.Interfaces;

/// <summary>
/// Per-server frontend runtime settings, written to config.js in the site folder on deploy or on demand.
/// </summary>
public interface IFrontendRuntimeConfigService
{
    Task<FrontendRuntimeConfigDto> GetAsync(int serverId);

    Task<FrontendRuntimeConfigDto> SaveAsync(int serverId, SaveFrontendRuntimeConfigRequest request);

    /// <summary>Writes config.js to the server now (no deployment needed).</summary>
    Task<ServerSetupActionResultDto> ApplyToServerAsync(int serverId);

    /// <summary>
    /// Writes config.js for a deployment. Returns null when the server has no runtime settings, in which case
    /// the config.js from the build stays in place.
    /// </summary>
    Task<(bool Success, string Message)?> WriteToServerAsync(ApplicationServer server);
}
