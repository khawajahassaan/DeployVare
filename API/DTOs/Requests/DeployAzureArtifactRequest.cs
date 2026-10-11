using System.ComponentModel.DataAnnotations;
using API.Models.Enums;

namespace API.DTOs.Requests;

public class DeployAzureArtifactRequest
{
    [Required]
    public int ApplicationServerId { get; set; }

    [Required]
    public ArtifactEnvironment Environment { get; set; }

    /// <summary>Build shown to the user; re-validated against the configured source before deploying.</summary>
    [Required]
    public int BuildId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ArtifactName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Version { get; set; }

    /// <summary>
    /// Defaults to WithoutLicense, so a caller that does not send the field deploys exactly as before.
    /// </summary>
    public LicensingMode LicensingMode { get; set; } = LicensingMode.WithoutLicense;

    /// <summary>Licence to install, required only when <see cref="LicensingMode"/> is WithLicense.</summary>
    public int? LicenseId { get; set; }

    /// <summary>Client-generated ID for polling live progress (GET api/apideployments/progress/{progressId}).</summary>
    [RegularExpression("^[A-Za-z0-9-]{8,64}$")]
    public string? ProgressId { get; set; }
}
