using System.ComponentModel.DataAnnotations;
using API.Models.Enums;

namespace API.DTOs.Requests;

public class SaveArtifactSourceRequest
{
    [Required]
    public int ApplicationId { get; set; }

    [Required]
    public ArtifactEnvironment Environment { get; set; }

    [Required]
    public ServerType ServerType { get; set; }

    [Required]
    [MaxLength(200)]
    public string Repository { get; set; } = string.Empty;

    [Required]
    [MaxLength(250)]
    public string Branch { get; set; } = string.Empty;

    public int? PipelineDefinitionId { get; set; }

    [MaxLength(200)]
    public string? ArtifactName { get; set; }

    [MaxLength(500)]
    public string? ArtifactSubfolder { get; set; }

    public bool PreserveWebConfig { get; set; } = true;

    /// <summary>Relative files/folders kept on the server when they already exist; separated by ";" or new lines.</summary>
    [MaxLength(4000)]
    public string? KeepOnServerPaths { get; set; }

    /// <summary>Backend sources only: run DbMigrator before deploying.</summary>
    public bool RunDatabaseMigrations { get; set; }

    [MaxLength(200)]
    public string? SqlScriptsArtifactName { get; set; }
}
