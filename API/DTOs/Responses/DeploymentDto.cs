namespace API.DTOs.Responses;

public class DeploymentDto
{
    public int Id { get; set; }
    public int DatabaseInstanceId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string ApplicationName { get; set; } = string.Empty;
    public string DeployedBy { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int FromPatchNumber { get; set; }
    public int ToPatchNumber { get; set; }
    public string? BackupFilePath { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationSeconds { get; set; }
    public List<DeploymentPatchDto> Patches { get; set; } = new();
    public List<DeploymentObjectDto> Objects { get; set; } = new();
}

public class DeploymentPatchDto
{
    public int PatchNumber { get; set; }
    public string PatchFileName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
    public DateTime ExecutedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class DeploymentObjectDto
{
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectName { get; set; } = string.Empty;
    public bool BackedUp { get; set; }
    public DateTime ExecutedAt { get; set; }
}



