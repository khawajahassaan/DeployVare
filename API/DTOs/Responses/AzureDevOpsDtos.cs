namespace API.DTOs.Responses;

/// <summary>Saved Azure DevOps connection. The token is never included.</summary>
public class AzureDevOpsConnectionDto
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string Organization { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public bool HasPersonalAccessToken { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public bool? LastTestSucceeded { get; set; }
}

public class AzureDevOpsTestResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class ArtifactSourceDto
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ServerType { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public int? PipelineDefinitionId { get; set; }
    public int? WindowsPipelineDefinitionId { get; set; }
    public string? ArtifactName { get; set; }
    public string? ArtifactSubfolder { get; set; }
    public bool PreserveWebConfig { get; set; }
    public string? KeepOnServerPaths { get; set; }
    public bool RunDatabaseMigrations { get; set; }
    public string? SqlScriptsArtifactName { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? UpdatedAt { get; set; }
}

public class AzureBuildArtifactDto
{
    public string Name { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public bool IsDownloadable { get; set; }
}

public class AzureBuildDto
{
    public int Id { get; set; }
    public string BuildNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public int PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public string Repository { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public DateTime? StartTime { get; set; }
    public DateTime? FinishTime { get; set; }
    public string? RequestedBy { get; set; }
    public string? WebUrl { get; set; }
    public List<AzureBuildArtifactDto> Artifacts { get; set; } = new();
}

/// <summary>Latest successful build for an application server + environment.</summary>
public class LatestBuildResultDto
{
    public ArtifactSourceDto Source { get; set; } = new();
    public AzureBuildDto Build { get; set; } = new();

    /// <summary>Artifact that will be deployed by default (configured name, or the only artifact).</summary>
    public string? SelectedArtifactName { get; set; }
}
