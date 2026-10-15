using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models;
using API.Models.Entities;
using API.Models.Enums;

namespace API.Services.Interfaces;

/// <summary>Everything needed to deploy an artifact to a server. Credentials are decrypted and must not be logged.</summary>
public sealed record ArtifactDeploymentContext(ApplicationServer Server, ArtifactSource Source, AzureDevOpsCredentials Credentials);

/// <summary>
/// Stores and resolves per-client Azure DevOps connections and per-application artifact sources.
/// </summary>
public interface IAzureDevOpsConfigService
{
    Task<AzureDevOpsConnectionDto?> GetConnectionAsync(int clientId);
    Task<AzureDevOpsConnectionDto> SaveConnectionAsync(SaveAzureDevOpsConnectionRequest request, string username);
    Task<bool> DeleteConnectionAsync(int clientId);
    Task<AzureDevOpsTestResultDto> TestConnectionAsync(TestAzureDevOpsConnectionRequest request, CancellationToken cancellationToken = default);

    Task<List<ArtifactSourceDto>> GetArtifactSourcesAsync(int clientId);
    Task<ArtifactSourceDto> CreateArtifactSourceAsync(SaveArtifactSourceRequest request, string username);
    Task<ArtifactSourceDto?> UpdateArtifactSourceAsync(int id, SaveArtifactSourceRequest request, string username);
    Task<bool> DeleteArtifactSourceAsync(int id);
    Task<AzureDevOpsTestResultDto> TestArtifactSourceAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Latest successful build for the artifact source matching the server's application, type and environment.</summary>
    Task<LatestBuildResultDto> GetLatestBuildAsync(int applicationServerId, ArtifactEnvironment environment, CancellationToken cancellationToken = default);

    Task<ArtifactDeploymentContext> ResolveDeploymentContextAsync(int applicationServerId, ArtifactEnvironment environment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Artifact source and credentials for an application's backend or frontend, without a registered server
    /// (Linux deployments connect to servers entered on the page).
    /// </summary>
    Task<(ArtifactSource Source, AzureDevOpsCredentials Credentials)> ResolveSourceAsync(int applicationId, ServerType serverType,
        ArtifactEnvironment environment, CancellationToken cancellationToken = default);
}
