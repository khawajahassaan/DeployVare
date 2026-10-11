using System.ComponentModel.DataAnnotations;

namespace API.DTOs.Requests;

public class SaveAzureDevOpsConnectionRequest
{
    [Required]
    public int ClientId { get; set; }

    /// <summary>Organization name ("Transviti") or URL ("https://dev.azure.com/Transviti").</summary>
    [Required]
    [MaxLength(200)]
    public string Organization { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Project { get; set; } = string.Empty;

    /// <summary>Required when creating; leave empty on update to keep the stored token.</summary>
    [MaxLength(500)]
    public string? PersonalAccessToken { get; set; }
}
