using API.DTOs.Requests;
using API.DTOs.Responses;

namespace API.Services.Interfaces;

public interface IApiDeploymentService
{
    /// <summary>
    /// Deploys API files to a remote server
    /// </summary>
    Task<ApiDeploymentResultDto> DeployAsync(DeployApiRequest request, int userId);

    /// <summary>
    /// Gets deployment history for a server
    /// </summary>
    Task<List<ApiDeploymentDto>> GetDeploymentHistoryAsync(int applicationServerId, int limit = 50);

    /// <summary>
    /// Gets a specific deployment by ID
    /// </summary>
    Task<ApiDeploymentDto?> GetDeploymentByIdAsync(int deploymentId);

    /// <summary>
    /// Downloads the backup file from a deployment
    /// </summary>
    Task<(bool Success, string? FilePath, string Message)> DownloadBackupAsync(int deploymentId);

    /// <summary>
    /// Deploys a frontend ZIP to a remote server
    /// </summary>
    Task<ApiDeploymentResultDto> DeployFrontendAsync(DeployFrontendRequest request, int userId);
}





