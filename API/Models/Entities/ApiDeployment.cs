using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using API.Models.Enums;

namespace API.Models.Entities;

public class ApiDeployment
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ApplicationServerId { get; set; }

    [ForeignKey(nameof(ApplicationServerId))]
    public ApplicationServer ApplicationServer { get; set; } = null!;

    [Required]
    public int DeployedBy { get; set; }

    [ForeignKey(nameof(DeployedBy))]
    public User DeployedByUser { get; set; } = null!;

    public DateTime DeploymentDate { get; set; } = DateTime.UtcNow;

    [MaxLength(50)]
    public string? Version { get; set; } // Application version (optional)

    [Required]
    public ApiDeploymentStatus Status { get; set; } = ApiDeploymentStatus.Pending;

    [MaxLength(500)]
    public string? BackupFileName { get; set; } // Name of backup zip on remote server

    [MaxLength(500)]
    public string? LocalBackupPath { get; set; } // Path where backup is downloaded to DeployVare server

    public AppDeploymentType DeploymentType { get; set; } = AppDeploymentType.Api;

    /// <summary>
    /// Whether this deployment enabled licensing on the server. Recorded so we can tell which clients have been
    /// migrated. Existing rows read back as WithoutLicense, which is what they were.
    /// </summary>
    public LicensingMode LicensingMode { get; set; } = LicensingMode.WithoutLicense;

    /// <summary>Licence installed by this deployment, when it was a licensed one.</summary>
    [MaxLength(50)]
    public string? LicenseId { get; set; }

    // Azure DevOps artifact traceability (null for manual uploads)
    [MaxLength(20)]
    public string? ArtifactEnvironment { get; set; }

    public int? AzureBuildId { get; set; }

    [MaxLength(100)]
    public string? AzureBuildNumber { get; set; }

    [MaxLength(200)]
    public string? AzureArtifactName { get; set; }

    [MaxLength(250)]
    public string? SourceBranch { get; set; }

    [MaxLength(64)]
    public string? SourceVersion { get; set; }

    public string? ErrorMessage { get; set; }

    public int? DeploymentDurationSeconds { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public ICollection<ApiDeploymentFile> DeploymentFiles { get; set; } = new List<ApiDeploymentFile>();
}





