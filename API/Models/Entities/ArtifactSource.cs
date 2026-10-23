using API.Models.Enums;

namespace API.Models.Entities;

/// <summary>
/// Where the build artifact for an application is retrieved from, per environment (Dev/UAT)
/// and server type (Api/Frontend).
/// </summary>
public class ArtifactSource
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public ArtifactEnvironment Environment { get; set; } = ArtifactEnvironment.Dev;
    public ServerType ServerType { get; set; } = ServerType.Api;
    public string Repository { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty; // Short name, e.g. "develop" (stored without refs/heads/)
    public string? ArtifactName { get; set; } // Optional; required only when a build publishes several artifacts
    public int? PipelineDefinitionId { get; set; } // Optional; restricts builds to a single pipeline
    public int? WindowsPipelineDefinitionId { get; set; } // Optional; Windows targets (IIS deploy, manual package) build from this pipeline

    /// <summary>The pipeline a Windows target takes builds from: the Windows one when set, else the shared one.</summary>
    public int? WindowsPipelineId => WindowsPipelineDefinitionId ?? PipelineDefinitionId;
    public string? ArtifactSubfolder { get; set; } // Optional folder inside the artifact that holds the deployable files, e.g. "AuditVare-WebApi"
    public bool PreserveWebConfig { get; set; } = true; // Keep the server's existing web.config instead of the artifact's

    // Relative files/folders kept on the server when they already exist (";"-separated), e.g. "appsettings.json;wwwroot"
    public string? KeepOnServerPaths { get; set; }

    // Backend sources: run DbMigrator.exe from the package with this build's SQL scripts artifact before deploying
    public bool RunDatabaseMigrations { get; set; }
    public string? SqlScriptsArtifactName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }

    // Navigation properties
    public Application Application { get; set; } = null!;
}
