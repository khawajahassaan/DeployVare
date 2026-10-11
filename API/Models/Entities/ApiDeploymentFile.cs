using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace API.Models.Entities;

public class ApiDeploymentFile
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ApiDeploymentId { get; set; }

    [ForeignKey(nameof(ApiDeploymentId))]
    public ApiDeployment ApiDeployment { get; set; } = null!;

    [Required]
    [MaxLength(200)]
    public string FileName { get; set; } = string.Empty;

    public long FileSize { get; set; } // Size in bytes

    [MaxLength(64)]
    public string? FileHash { get; set; } // SHA256 hash for verification

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}





