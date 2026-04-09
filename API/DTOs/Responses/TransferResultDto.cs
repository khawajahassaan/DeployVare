namespace API.DTOs.Responses;

public class TransferResultDto
{
    public int DeploymentId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}




