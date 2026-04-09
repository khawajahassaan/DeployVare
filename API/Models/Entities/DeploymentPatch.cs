namespace API.Models.Entities;

public class DeploymentPatch
{
    public int Id { get; set; }
    public int DeploymentId { get; set; }
    public int PatchNumber { get; set; }
    public string PatchFileName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Author { get; set; }
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Success"; // Success, Failed
    public string? ErrorMessage { get; set; }

    // Navigation property
    public Deployment Deployment { get; set; } = null!;
}



