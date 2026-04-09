using API.Models.Enums;

namespace API.DTOs.Responses;

public class ApiDeploymentResultDto
{
    public int DeploymentId { get; set; }
    public ApiDeploymentStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? BackupFileName { get; set; }
    public int DeploymentDurationSeconds { get; set; }
    public List<string> DeploymentSteps { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public List<ApiDeploymentFileDto> DeployedFiles { get; set; } = new();
}





