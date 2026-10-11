using API.DTOs.Responses;

namespace API.Services.Interfaces;

public interface IDeploymentService
{
    Task<DeploymentResultDto> DeployAsync(int databaseInstanceId, string zipFilePath, string deployedBy);
}




