using System.ComponentModel.DataAnnotations;

namespace API.DTOs.Requests;

/// <summary>
/// Tests Azure DevOps access. Any value left empty falls back to the client's saved connection,
/// so unsaved form values can be tested without re-entering the stored token.
/// </summary>
public class TestAzureDevOpsConnectionRequest
{
    [Required]
    public int ClientId { get; set; }

    [MaxLength(200)]
    public string? Organization { get; set; }

    [MaxLength(200)]
    public string? Project { get; set; }

    [MaxLength(500)]
    public string? PersonalAccessToken { get; set; }
}
