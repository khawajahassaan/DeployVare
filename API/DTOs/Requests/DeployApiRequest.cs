using System.ComponentModel.DataAnnotations;

namespace API.DTOs.Requests;

public class DeployApiRequest
{
    [Required]
    public int ApplicationServerId { get; set; }

    [MaxLength(50)]
    public string? Version { get; set; }

    [Required]
    public List<IFormFile> Files { get; set; } = new();
}





