namespace API.Models.Entities;

public class Client
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ClientCode { get; set; } = string.Empty; // The "Client ID" shown in the UI
    public string LicenseKey { get; set; } = string.Empty; // 32-char GUID (no dashes), unique per client
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
