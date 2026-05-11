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

    public string? ErrorMessage { get; set; }

    public int? DeploymentDurationSeconds { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    // Navigation properties
    public ICollection<ApiDeploymentFile> DeploymentFiles { get; set; } = new List<ApiDeploymentFile>();
}





