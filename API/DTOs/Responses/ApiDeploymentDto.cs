using API.Models.Enums;

namespace API.DTOs.Responses;

public class ApiDeploymentDto
{
    public int Id { get; set; }
    public int ApplicationServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string DeployedBy { get; set; } = string.Empty;
    public DateTime DeploymentDate { get; set; }
    public string? Version { get; set; }
    public ApiDeploymentStatus Status { get; set; }
    public string? BackupFileName { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DeploymentDurationSeconds { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<ApiDeploymentFileDto> Files { get; set; } = new();
}

public class ApiDeploymentFileDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string? FileHash { get; set; }
    public DateTime UploadedAt { get; set; }
}





