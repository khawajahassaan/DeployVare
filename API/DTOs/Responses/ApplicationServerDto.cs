namespace API.DTOs.Responses;

public class ApplicationServerDto
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string HostName { get; set; } = string.Empty;
    public int Port { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DeploymentPath { get; set; } = string.Empty;
    public string AppPoolName { get; set; } = string.Empty;
    public string WebsiteName { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string ServerType { get; set; } = "Api";
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int TotalDeployments { get; set; }
    public DateTime? LastDeploymentDate { get; set; }
}


