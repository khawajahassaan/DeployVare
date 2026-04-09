namespace API.Models.Entities;

public class Application
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;

    // Navigation properties
    public ICollection<DatabaseInstance> DatabaseInstances { get; set; } = new List<DatabaseInstance>();
}



