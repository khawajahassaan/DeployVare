using API.Models.Enums;

namespace API.Models.Entities;

public class Deployment
{
    public int Id { get; set; }
    public int DatabaseInstanceId { get; set; }
    public string DeployedBy { get; set; } = string.Empty;
    public DateTime DeployedAt { get; set; } = DateTime.UtcNow;
    public DeploymentStatus Status { get; set; } = DeploymentStatus.Pending;
    public int FromPatchNumber { get; set; }
    public int ToPatchNumber { get; set; }
    public string? BackupFilePath { get; set; }
    public string? ErrorMessage { get; set; }
    public int? DurationSeconds { get; set; }

    // Navigation properties
    public DatabaseInstance DatabaseInstance { get; set; } = null!;
    public ICollection<DeploymentPatch> DeploymentPatches { get; set; } = new List<DeploymentPatch>();
    public ICollection<DeploymentObject> DeploymentObjects { get; set; } = new List<DeploymentObject>();
}



