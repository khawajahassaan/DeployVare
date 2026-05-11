using System.ComponentModel.DataAnnotations;

namespace API.DTOs.Requests;

public class DeployFrontendRequest
{
    [Required]
    public int ApplicationServerId { get; set; }

    [MaxLength(50)]
    public string? Version { get; set; }

    [Required]
    public IFormFile ZipFile { get; set; } = null!;
}
