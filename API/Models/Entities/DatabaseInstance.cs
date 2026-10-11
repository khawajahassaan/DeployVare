namespace API.Models.Entities;

public class DatabaseInstance
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string InstanceName { get; set; } = string.Empty;
    public string ConnectionStringEncrypted { get; set; } = string.Empty;
    public int CurrentPatchNumber { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation properties
    public Application Application { get; set; } = null!;
    public ICollection<Deployment> Deployments { get; set; } = new List<Deployment>();
}



