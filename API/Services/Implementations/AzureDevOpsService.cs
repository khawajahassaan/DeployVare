using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using API.DTOs.Responses;
using API.Models;
using API.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace API.Services.Implementations;

public class AzureDevOpsService : IAzureDevOpsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly AzureDevOpsOptions _options;
    private readonly ILogger<AzureDevOpsService> _logger;

    public AzureDevOpsService(HttpClient httpClient, IOptions<AzureDevOpsOptions> options, ILogger<AzureDevOpsService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task TestConnectionAsync(AzureDevOpsCredentials credentials, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AzureDevOpsConnectionStarted for {Target}", credentials);

        try
        {
            await GetProjectAsync(credentials, cancellationToken);
            _logger.LogInformation("AzureDevOpsConnectionSucceeded for {Target}", credentials);
        }
        catch (AzureDevOpsException ex)
        {
            _logger.LogWarning("AzureDevOpsConnectionFailed for {Target}: {ErrorCode} {Message}", credentials, ex.ErrorCode, ex.Message);
            throw;
        }
    }

    public async Task ValidateRepositoryAsync(AzureDevOpsCredentials credentials, string repository, string branch,
        CancellationToken cancellationToken = default)
    {
        var repo = await GetRepositoryAsync(credentials, repository, cancellationToken);
        await EnsureBranchExistsAsync(credentials, repo, AzureDevOpsCredentials.NormalizeBranch(branch), cancellationToken);
    }

    public async Task<AzureBuildDto> GetLatestSuccessfulBuildAsync(AzureDevOpsCredentials credentials, string repository, string branch,
        int? pipelineDefinitionId, string? artifactName, CancellationToken cancellationToken = default)
    {
        var branchName = AzureDevOpsCredentials.NormalizeBranch(branch);
        _logger.LogInformation("LatestBuildLookupStarted for {Target} repository {Repository} branch {Branch} pipeline {PipelineId}",
            credentials, repository, branchName, pipelineDefinitionId);

        var project = await GetProjectAsync(credentials, cancellationToken);
        var repo = await GetRepositoryAsync(credentials, repository, cancellationToken);
        await EnsureBranchExistsAsync(credentials, repo, branchName, cancellationToken);

        var query = new StringBuilder(ProjectUrl(credentials, "_apis/build/builds"))
            .Append("?repositoryId=").Append(Uri.EscapeDataString(repo.Id))
            .Append("&repositoryType=TfsGit")
            .Append("&branchName=").Append(Uri.EscapeDataString($"refs/heads/{branchName}"))
            .Append("&statusFilter=completed&resultFilter=succeeded&queryOrder=finishTimeDescending")
            .Append("&$top=").Append(Math.Max(1, _options.MaxBuildsToInspect))
            .Append("&api-version=").Append(_options.ApiVersion);

        if (pipelineDefinitionId.HasValue)
            query.Append("&definitions=").Append(pipelineDefinitionId.Value);

        var builds = await GetJsonAsync<AdoList<AdoBuild>>(credentials, query.ToString(), NotFoundAs.Project, cancellationToken);

        var candidates = builds.Value
            .Where(b => IsEligibleBuild(b, project.Id, repo.Id, branchName, pipelineDefinitionId))
            .OrderByDescending(b => b.FinishTime)
            .ToList();

        if (candidates.Count == 0)
        {
            var pipelineText = pipelineDefinitionId.HasValue ? $" in pipeline {pipelineDefinitionId}" : string.Empty;
            throw new AzureDevOpsException(AzureDevOpsErrorCode.NoSuccessfulBuild,
                $"No successful build found for repository '{repo.Name}' on branch '{branchName}'{pipelineText}.");
        }

        var sawArtifacts = false;
        foreach (var build in candidates)
        {
            var artifacts = await GetBuildArtifactsAsync(credentials, build.Id, cancellationToken);
            if (artifacts.Count > 0) sawArtifacts = true;

            var hasUsableArtifact = string.IsNullOrWhiteSpace(artifactName)
                ? artifacts.Any(a => a.IsDownloadable)
                : artifacts.Any(a => a.IsDownloadable && a.Name.Equals(artifactName, StringComparison.OrdinalIgnoreCase));

            if (!hasUsableArtifact)
            {
                _logger.LogInformation("Skipping build {BuildId}: no usable artifact{ArtifactText}", build.Id,
                    string.IsNullOrWhiteSpace(artifactName) ? string.Empty : $" named '{artifactName}'");
                continue;
            }

            var dto = MapBuild(build, repo.Name, artifacts);
            _logger.LogInformation("LatestBuildFound {BuildId} #{BuildNumber} for {Target} repository {Repository} branch {Branch}",
                dto.Id, dto.BuildNumber, credentials, repo.Name, branchName);
            return dto;
        }

        if (!string.IsNullOrWhiteSpace(artifactName) && sawArtifacts)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotFound,
                $"None of the {candidates.Count} most recent successful builds publish a downloadable artifact named '{artifactName}'.");

        throw new AzureDevOpsException(AzureDevOpsErrorCode.NoArtifacts,
            $"The {candidates.Count} most recent successful builds for '{repo.Name}' on '{branchName}' have no downloadable artifacts.");
    }

    public async Task<AzureBuildDto> GetEligibleBuildAsync(AzureDevOpsCredentials credentials, int buildId, string repository, string branch,
        int? pipelineDefinitionId, CancellationToken cancellationToken = default)
    {
        var branchName = AzureDevOpsCredentials.NormalizeBranch(branch);
        var project = await GetProjectAsync(credentials, cancellationToken);
        var repo = await GetRepositoryAsync(credentials, repository, cancellationToken);

        var url = ProjectUrl(credentials, $"_apis/build/builds/{buildId}") + $"?api-version={_options.ApiVersion}";
        var build = await GetJsonAsync<AdoBuild>(credentials, url, NotFoundAs.Build, cancellationToken);

        if (!IsEligibleBuild(build, project.Id, repo.Id, branchName, pipelineDefinitionId))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.BuildNotEligible,
                $"Build {buildId} is not a successful build of repository '{repo.Name}' on branch '{branchName}'"
                + (pipelineDefinitionId.HasValue ? $" in pipeline {pipelineDefinitionId}." : "."));

        var artifacts = await GetBuildArtifactsAsync(credentials, buildId, cancellationToken);
        return MapBuild(build, repo.Name, artifacts);
    }

    public async Task<List<AzureBuildArtifactDto>> GetBuildArtifactsAsync(AzureDevOpsCredentials credentials, int buildId,
        CancellationToken cancellationToken = default)
    {
        var url = ProjectUrl(credentials, $"_apis/build/builds/{buildId}/artifacts") + $"?api-version={_options.ApiVersion}";
        var artifacts = await GetJsonAsync<AdoList<AdoArtifact>>(credentials, url, NotFoundAs.Build, cancellationToken);

        return artifacts.Value.Select(a => new AzureBuildArtifactDto
        {
            Name = a.Name,
            ResourceType = a.Resource?.Type ?? string.Empty,
            SizeBytes = a.Resource?.GetArtifactSize(),
            IsDownloadable = IsDownloadableArtifact(a)
        }).ToList();
    }

    public async Task DownloadArtifactAsync(AzureDevOpsCredentials credentials, int buildId, string artifactName, string destinationZipPath,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("ArtifactDownloadStarted build {BuildId} artifact {ArtifactName} for {Target}", buildId, artifactName, credentials);

        try
        {
            var metadataUrl = ProjectUrl(credentials, $"_apis/build/builds/{buildId}/artifacts")
                + $"?artifactName={Uri.EscapeDataString(artifactName)}&api-version={_options.ApiVersion}";
            var artifact = await GetJsonAsync<AdoArtifact>(credentials, metadataUrl, NotFoundAs.Artifact, cancellationToken);

            if (!IsDownloadableArtifact(artifact))
                throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotDownloadable,
                    $"Artifact '{artifactName}' (type '{artifact.Resource?.Type}') cannot be downloaded through the REST API.");

            var downloadUri = new Uri(artifact.Resource!.DownloadUrl!);
            if (!IsTrustedAzureDevOpsHost(downloadUri))
                throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotDownloadable,
                    $"Artifact download URL host '{downloadUri.Host}' is not a trusted Azure DevOps host.");

            var maxBytes = _options.MaxArtifactSizeMB * 1024 * 1024;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(Math.Max(1, _options.DownloadTimeoutMinutes)));

            using var request = CreateRequest(credentials, downloadUri);
            using var response = await SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token, cancellationToken);
            await EnsureSuccessAsync(response, NotFoundAs.Artifact, cancellationToken);

            if (response.Content.Headers.ContentLength is long declared && declared > maxBytes)
                throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactTooLarge,
                    $"Artifact '{artifactName}' is {declared / (1024 * 1024)} MB, above the {_options.MaxArtifactSizeMB} MB limit.");

            Directory.CreateDirectory(Path.GetDirectoryName(destinationZipPath)!);

            long total = 0;
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var target = new FileStream(destinationZipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    total += read;
                    if (total > maxBytes)
                        throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactTooLarge,
                            $"Artifact '{artifactName}' exceeds the {_options.MaxArtifactSizeMB} MB limit.");
                    await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AzureDevOpsException(AzureDevOpsErrorCode.Timeout,
                    $"Downloading artifact '{artifactName}' timed out after {_options.DownloadTimeoutMinutes} minutes.");
            }
            catch (IOException ex)
            {
                throw new AzureDevOpsException(AzureDevOpsErrorCode.DownloadFailed, $"Downloading artifact '{artifactName}' failed: {ex.Message}", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new AzureDevOpsException(AzureDevOpsErrorCode.DownloadFailed, $"Downloading artifact '{artifactName}' was interrupted.", ex);
            }

            _logger.LogInformation("ArtifactDownloadSucceeded build {BuildId} artifact {ArtifactName} ({Bytes} bytes)", buildId, artifactName, total);
        }
        catch (AzureDevOpsException ex)
        {
            TryDelete(destinationZipPath);
            _logger.LogWarning("ArtifactDownloadFailed build {BuildId} artifact {ArtifactName}: {ErrorCode} {Message}",
                buildId, artifactName, ex.ErrorCode, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// A build is eligible only when it belongs to the configured project, repository, branch and (optionally)
    /// pipeline, and completed successfully. Build IDs alone are never trusted.
    /// </summary>
    internal static bool IsEligibleBuild(AdoBuild build, string projectId, string repositoryId, string branchName, int? pipelineDefinitionId)
    {
        return string.Equals(build.Project?.Id, projectId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(build.Repository?.Id, repositoryId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(build.SourceBranch, $"refs/heads/{branchName}", StringComparison.OrdinalIgnoreCase)
            && string.Equals(build.Status, "completed", StringComparison.OrdinalIgnoreCase)
            && string.Equals(build.Result, "succeeded", StringComparison.OrdinalIgnoreCase)
            && (!pipelineDefinitionId.HasValue || build.Definition?.Id == pipelineDefinitionId.Value);
    }

    internal static bool IsTrustedAzureDevOpsHost(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        var host = uri.Host;
        return host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".dev.azure.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDownloadableArtifact(AdoArtifact artifact)
    {
        var type = artifact.Resource?.Type;
        var downloadable = type is not null
            && (type.Equals("Container", StringComparison.OrdinalIgnoreCase) || type.Equals("PipelineArtifact", StringComparison.OrdinalIgnoreCase));

        return downloadable
            && Uri.TryCreate(artifact.Resource!.DownloadUrl, UriKind.Absolute, out var uri)
            && IsTrustedAzureDevOpsHost(uri);
    }

    private async Task<AdoProject> GetProjectAsync(AzureDevOpsCredentials credentials, CancellationToken cancellationToken)
    {
        var url = $"{OrganizationUrl(credentials)}/_apis/projects/{Uri.EscapeDataString(credentials.Project)}?api-version={_options.ApiVersion}";
        return await GetJsonAsync<AdoProject>(credentials, url, NotFoundAs.Project, cancellationToken);
    }

    private async Task<AdoRepository> GetRepositoryAsync(AzureDevOpsCredentials credentials, string repository, CancellationToken cancellationToken)
    {
        var name = AzureDevOpsCredentials.ValidateName(repository, "Repository");
        var url = ProjectUrl(credentials, $"_apis/git/repositories/{Uri.EscapeDataString(name)}") + $"?api-version={_options.ApiVersion}";
        return await GetJsonAsync<AdoRepository>(credentials, url, NotFoundAs.Repository, cancellationToken);
    }

    private async Task EnsureBranchExistsAsync(AzureDevOpsCredentials credentials, AdoRepository repo, string branchName, CancellationToken cancellationToken)
    {
        // The refs filter is a prefix match, so the exact ref name is checked below
        var url = ProjectUrl(credentials, $"_apis/git/repositories/{Uri.EscapeDataString(repo.Id)}/refs")
            + $"?filter={Uri.EscapeDataString($"heads/{branchName}")}&api-version={_options.ApiVersion}";
        var refs = await GetJsonAsync<AdoList<AdoRef>>(credentials, url, NotFoundAs.Repository, cancellationToken);

        if (!refs.Value.Any(r => string.Equals(r.Name, $"refs/heads/{branchName}", StringComparison.OrdinalIgnoreCase)))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.BranchNotFound,
                $"Branch '{branchName}' does not exist in repository '{repo.Name}'.");
    }

    private async Task<T> GetJsonAsync<T>(AzureDevOpsCredentials credentials, string url, NotFoundAs notFoundAs, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.RequestTimeoutSeconds)));

        using var request = CreateRequest(credentials, new Uri(url));
        using var response = await SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token, cancellationToken);
        await EnsureSuccessAsync(response, notFoundAs, cancellationToken);

        try
        {
            var body = await response.Content.ReadAsStringAsync(timeout.Token);
            return JsonSerializer.Deserialize<T>(body, JsonOptions)
                ?? throw new JsonException("Empty response body");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Unexpected Azure DevOps response from {Path}", request.RequestUri!.AbsolutePath);
            throw new AzureDevOpsException(AzureDevOpsErrorCode.RequestFailed, "Azure DevOps returned an unexpected response.", ex);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completion,
        CancellationToken timeoutToken, CancellationToken callerToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, completion, timeoutToken);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            throw new AzureDevOpsException(AzureDevOpsErrorCode.Timeout, "Azure DevOps did not respond in time.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Azure DevOps request to {Host}{Path} failed", request.RequestUri!.Host, request.RequestUri.AbsolutePath);
            throw new AzureDevOpsException(AzureDevOpsErrorCode.RequestFailed, "Could not reach Azure DevOps. Check network connectivity.", ex);
        }
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, NotFoundAs notFoundAs, CancellationToken cancellationToken)
    {
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

        // An invalid or expired token is answered with a 203 HTML sign-in page instead of a 401
        if (response.StatusCode == HttpStatusCode.NonAuthoritativeInformation
            || (response.IsSuccessStatusCode && mediaType.Contains("html", StringComparison.OrdinalIgnoreCase)))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.AuthenticationFailed,
                "Azure DevOps authentication failed. The personal access token is invalid or expired.");

        if (response.IsSuccessStatusCode) return;

        var detail = await ReadErrorMessageAsync(response, cancellationToken);
        _logger.LogWarning("Azure DevOps returned {StatusCode} for {Path}: {Detail}",
            (int)response.StatusCode, response.RequestMessage?.RequestUri?.AbsolutePath, detail);

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new AzureDevOpsException(AzureDevOpsErrorCode.AuthenticationFailed,
                "Azure DevOps authentication failed. The personal access token is invalid or expired."),
            HttpStatusCode.Forbidden => new AzureDevOpsException(AzureDevOpsErrorCode.AccessDenied,
                "The personal access token does not have access to this resource. It needs the Build (Read) and Code (Read) scopes."),
            HttpStatusCode.NotFound => notFoundAs switch
            {
                NotFoundAs.Project => new AzureDevOpsException(AzureDevOpsErrorCode.ProjectNotFound,
                    "Azure DevOps project was not found, or the token cannot access it."),
                NotFoundAs.Repository => new AzureDevOpsException(AzureDevOpsErrorCode.RepositoryNotFound,
                    "Repository was not found in the Azure DevOps project."),
                NotFoundAs.Build => new AzureDevOpsException(AzureDevOpsErrorCode.BuildNotFound, "Build was not found."),
                _ => new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotFound, "Artifact was not found on this build.")
            },
            _ when (int)response.StatusCode >= 500 => new AzureDevOpsException(AzureDevOpsErrorCode.RequestFailed,
                $"Azure DevOps is unavailable (HTTP {(int)response.StatusCode}). Try again later."),
            _ => new AzureDevOpsException(AzureDevOpsErrorCode.RequestFailed,
                $"Azure DevOps request failed (HTTP {(int)response.StatusCode}){(string.IsNullOrEmpty(detail) ? "." : $": {detail}")}")
        };
    }

    private static async Task<string> ReadErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var message))
                return message.GetString() is { } text && text.Length > 300 ? text[..300] : message.GetString() ?? string.Empty;
        }
        catch
        {
            // Non-JSON error bodies are not surfaced
        }
        return string.Empty;
    }

    private HttpRequestMessage CreateRequest(AzureDevOpsCredentials credentials, Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{credentials.PersonalAccessToken}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private string OrganizationUrl(AzureDevOpsCredentials credentials) =>
        $"{_options.BaseUrl.TrimEnd('/')}/{Uri.EscapeDataString(credentials.Organization)}";

    private string ProjectUrl(AzureDevOpsCredentials credentials, string relativePath) =>
        $"{OrganizationUrl(credentials)}/{Uri.EscapeDataString(credentials.Project)}/{relativePath}";

    private static AzureBuildDto MapBuild(AdoBuild build, string repositoryName, List<AzureBuildArtifactDto> artifacts) => new()
    {
        Id = build.Id,
        BuildNumber = build.BuildNumber ?? string.Empty,
        Status = build.Status ?? string.Empty,
        Result = build.Result ?? string.Empty,
        PipelineId = build.Definition?.Id ?? 0,
        PipelineName = build.Definition?.Name ?? string.Empty,
        Repository = repositoryName,
        SourceBranch = build.SourceBranch ?? string.Empty,
        SourceVersion = build.SourceVersion ?? string.Empty,
        StartTime = build.StartTime,
        FinishTime = build.FinishTime,
        RequestedBy = build.RequestedFor?.DisplayName,
        WebUrl = build.Links?.Web?.Href,
        Artifacts = artifacts
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private enum NotFoundAs { Project, Repository, Build, Artifact }
}

// Azure DevOps REST response shapes (only the fields DeployVare uses)
internal sealed class AdoList<T>
{
    public int Count { get; set; }
    public List<T> Value { get; set; } = new();
}

internal sealed class AdoProject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal sealed class AdoRepository
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

internal sealed class AdoRef
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class AdoBuild
{
    public int Id { get; set; }
    public string? BuildNumber { get; set; }
    public string? Status { get; set; }
    public string? Result { get; set; }
    public string? SourceBranch { get; set; }
    public string? SourceVersion { get; set; }
    public DateTime? StartTime { get; set; }
    public DateTime? FinishTime { get; set; }
    public AdoIdRef? Project { get; set; }
    public AdoIdRef? Repository { get; set; }
    public AdoDefinitionRef? Definition { get; set; }
    public AdoIdentity? RequestedFor { get; set; }

    [JsonPropertyName("_links")]
    public AdoLinks? Links { get; set; }
}

internal sealed class AdoIdRef
{
    public string? Id { get; set; }
    public string? Name { get; set; }
}

internal sealed class AdoDefinitionRef
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

internal sealed class AdoIdentity
{
    public string? DisplayName { get; set; }
}

internal sealed class AdoLinks
{
    public AdoLink? Web { get; set; }
}

internal sealed class AdoLink
{
    public string? Href { get; set; }
}

internal sealed class AdoArtifact
{
    public string Name { get; set; } = string.Empty;
    public AdoArtifactResource? Resource { get; set; }
}

internal sealed class AdoArtifactResource
{
    public string? Type { get; set; }
    public string? DownloadUrl { get; set; }
    public Dictionary<string, JsonElement>? Properties { get; set; }

    public long? GetArtifactSize()
    {
        if (Properties is null || !Properties.TryGetValue("artifactsize", out var size)) return null;
        if (size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var number)) return number;
        return size.ValueKind == JsonValueKind.String && long.TryParse(size.GetString(), out var parsed) ? parsed : null;
    }
}
