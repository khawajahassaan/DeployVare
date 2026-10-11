namespace API.DTOs.Responses;

public class DeploymentResultDto
{
    public int DeploymentId { get; set; }
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int FromPatchNumber { get; set; }
    public int ToPatchNumber { get; set; }
    public int PatchesApplied { get; set; }
    public int ProceduresUpdated { get; set; }
    public int FunctionsUpdated { get; set; }
    public string? BackupFileName { get; set; }
    public string? ErrorMessage { get; set; }
    public int DurationSeconds { get; set; }
}



