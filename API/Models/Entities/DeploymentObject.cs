namespace API.Models.Entities;

public class DeploymentObject
{
    public int Id { get; set; }
    public int DeploymentId { get; set; }
    public string ObjectType { get; set; } = string.Empty; // StoredProcedure, Function
    public string ObjectName { get; set; } = string.Empty;
    public bool BackedUp { get; set; } = false;
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public Deployment Deployment { get; set; } = null!;
}



