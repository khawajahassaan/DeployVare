using API.DTOs.Responses;
using API.Models;

namespace API.Services.Interfaces;

/// <summary>
/// Azure DevOps Services REST API access. All failures are raised as <see cref="AzureDevOpsException"/>.
/// </summary>
public interface IAzureDevOpsService
{
    /// <summary>Verifies the token can authenticate and read the configured project.</summary>
    Task TestConnectionAsync(AzureDevOpsCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Verifies the repository exists in the project and the branch exists in the repository.</summary>
    Task ValidateRepositoryAsync(AzureDevOpsCredentials credentials, string repository, string branch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Latest completed, succeeded build of the given repository/branch (and pipeline, when set) in the
    /// configured project that has a downloadable artifact (the named one, when set).
    /// </summary>
    Task<AzureBuildDto> GetLatestSuccessfulBuildAsync(AzureDevOpsCredentials credentials, string repository, string branch,
        int? pipelineDefinitionId, string? artifactName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a specific build after re-checking that it belongs to the repository/branch (and pipeline),
    /// completed and succeeded.
    /// </summary>
    Task<AzureBuildDto> GetEligibleBuildAsync(AzureDevOpsCredentials credentials, int buildId, string repository, string branch,
        int? pipelineDefinitionId, CancellationToken cancellationToken = default);

    Task<List<AzureBuildArtifactDto>> GetBuildArtifactsAsync(AzureDevOpsCredentials credentials, int buildId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads an artifact as a ZIP file to <paramref name="destinationZipPath"/>. <paramref name="onProgress"/>
    /// receives (bytes so far, total bytes when the server declares it) about twice a second.
    /// </summary>
    Task DownloadArtifactAsync(AzureDevOpsCredentials credentials, int buildId, string artifactName, string destinationZipPath,
        CancellationToken cancellationToken = default, Action<long, long?>? onProgress = null);
}
