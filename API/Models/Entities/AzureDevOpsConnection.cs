namespace API.Models.Entities;

/// <summary>
/// Azure DevOps organization/project access for a client. The personal access token is
/// stored encrypted and is never returned to the frontend.
/// </summary>
public class AzureDevOpsConnection
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Organization { get; set; } = string.Empty; // Organization name, e.g. "contoso"
    public string Project { get; set; } = string.Empty;
    public string EncryptedPersonalAccessToken { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public bool? LastTestSucceeded { get; set; }

    // Navigation properties
    public Client Client { get; set; } = null!;
}
