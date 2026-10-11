using System.Net;
using System.Text;
using System.Text.Json;
using API.Models;
using API.Services;
using API.Services.Implementations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace API.Tests.AzureDevOps;

public class AzureDevOpsServiceTests : IDisposable
{
    private const string Token = "super-secret-test-token";
    private const string ProjectPath = "/Transviti/_apis/projects/AuditVare_Release_2";
    private const string ProjectBase = "/Transviti/AuditVare_Release_2";

    private static readonly AzureDevOpsCredentials Credentials =
        AzureDevOpsCredentials.Create("Transviti", "AuditVare_Release_2", Token);

    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory)) Directory.Delete(_tempDirectory, true);
    }

    [Fact]
    public async Task TestConnection_Succeeds_AndSendsBasicAuthWithToken()
    {
        var handler = new FakeHttpMessageHandler().OnJson(PathIs(ProjectPath), ProjectJson);

        await CreateService(handler).TestConnectionAsync(Credentials);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https", request.RequestUri!.Scheme);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + Token)), request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task TestConnection_InvalidToken_203SignInPage_ThrowsAuthenticationFailed()
    {
        var handler = new FakeHttpMessageHandler().On(PathIs(ProjectPath), _ => new HttpResponseMessage(HttpStatusCode.NonAuthoritativeInformation)
        {
            Content = new StringContent("<html>Sign in</html>", Encoding.UTF8, "text/html")
        });

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() => CreateService(handler).TestConnectionAsync(Credentials));

        Assert.Equal(AzureDevOpsErrorCode.AuthenticationFailed, ex.ErrorCode);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, AzureDevOpsErrorCode.AuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, AzureDevOpsErrorCode.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, AzureDevOpsErrorCode.ProjectNotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable, AzureDevOpsErrorCode.RequestFailed)]
    public async Task TestConnection_MapsHttpFailures(HttpStatusCode status, AzureDevOpsErrorCode expected)
    {
        var handler = new FakeHttpMessageHandler().OnJson(PathIs(ProjectPath), "{\"message\":\"failure\"}", status);

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() => CreateService(handler).TestConnectionAsync(Credentials));

        Assert.Equal(expected, ex.ErrorCode);
        Assert.DoesNotContain(Token, ex.Message);
    }

    [Fact]
    public async Task TestConnection_SlowResponse_ThrowsTimeout()
    {
        var handler = new FakeHttpMessageHandler().OnAsync(PathIs(ProjectPath), async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return FakeHttpMessageHandler.Json(ProjectJson);
        });

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler, new AzureDevOpsOptions { RequestTimeoutSeconds = 1 }).TestConnectionAsync(Credentials));

        Assert.Equal(AzureDevOpsErrorCode.Timeout, ex.ErrorCode);
    }

    [Fact]
    public async Task LatestBuild_IgnoresBuildsFromOtherPipelineBranchRepositoryOrResult()
    {
        var builds = Builds(
            Build(10, pipelineId: 59),                      // other pipeline building the same repo (e.g. cfg)
            Build(9, branch: "refs/heads/feature/x"),       // other branch
            Build(8, result: "partiallySucceeded"),          // not succeeded
            Build(7, repositoryId: "other-repo"),            // other repository
            Build(6, projectId: "other-project"),            // other project
            Build(5));                                       // eligible

        var handler = BaseRoutes()
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds"), builds)
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"), Artifacts(Artifact("drop")));

        var build = await CreateService(handler).GetLatestSuccessfulBuildAsync(Credentials, "auditvare-uat-backend", "main", 36, "drop");

        Assert.Equal(5, build.Id);
        Assert.Equal("drop", Assert.Single(build.Artifacts).Name);
    }

    [Fact]
    public async Task LatestBuild_SendsRepositoryBranchPipelineAndResultFilters()
    {
        var handler = BaseRoutes()
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds"), Builds(Build(5)))
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"), Artifacts(Artifact("drop")));

        await CreateService(handler).GetLatestSuccessfulBuildAsync(Credentials, "auditvare-uat-backend", "refs/heads/main", 36, null);

        var query = handler.Requests.Single(r => r.RequestUri!.AbsolutePath == $"{ProjectBase}/_apis/build/builds").RequestUri!.Query;
        Assert.Contains("repositoryId=repo-1", query);
        Assert.Contains("repositoryType=TfsGit", query);
        Assert.Contains("branchName=refs%2Fheads%2Fmain", query);
        Assert.Contains("statusFilter=completed", query);
        Assert.Contains("resultFilter=succeeded", query);
        Assert.Contains("definitions=36", query);
    }

    [Fact]
    public async Task LatestBuild_SkipsNewerBuildWithoutConfiguredArtifact()
    {
        var handler = BaseRoutes()
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds"), Builds(Build(6), Build(5)))
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/6/artifacts"), Artifacts(Artifact("sqlscripts")))
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"), Artifacts(Artifact("drop"), Artifact("sqlscripts")));

        var build = await CreateService(handler).GetLatestSuccessfulBuildAsync(Credentials, "auditvare-uat-backend", "main", null, "drop");

        Assert.Equal(5, build.Id);
    }

    [Fact]
    public async Task LatestBuild_NoSuccessfulBuilds_ThrowsNoSuccessfulBuild()
    {
        var handler = BaseRoutes().OnJson(PathIs($"{ProjectBase}/_apis/build/builds"), Builds());

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).GetLatestSuccessfulBuildAsync(Credentials, "auditvare-uat-backend", "main", 36, "drop"));

        Assert.Equal(AzureDevOpsErrorCode.NoSuccessfulBuild, ex.ErrorCode);
    }

    [Fact]
    public async Task LatestBuild_ConfiguredArtifactMissingEverywhere_ThrowsArtifactNotFound()
    {
        var handler = BaseRoutes()
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds"), Builds(Build(5)))
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"), Artifacts(Artifact("frontend-drop")));

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).GetLatestSuccessfulBuildAsync(Credentials, "auditvare-uat-backend", "main", null, "drop"));

        Assert.Equal(AzureDevOpsErrorCode.ArtifactNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task LatestBuild_BuildsWithoutArtifacts_ThrowsNoArtifacts()
    {
        var handler = BaseRoutes()
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds"), Builds(Build(5)))
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"), Artifacts());

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).GetLatestSuccessfulBuildAsync(Credentials, "auditvare-uat-backend", "main", null, null));

        Assert.Equal(AzureDevOpsErrorCode.NoArtifacts, ex.ErrorCode);
    }

    [Fact]
    public async Task LatestBuild_BranchOnlyMatchesByPrefix_ThrowsBranchNotFound()
    {
        var handler = new FakeHttpMessageHandler()
            .OnJson(PathIs(ProjectPath), ProjectJson)
            .OnJson(PathIs($"{ProjectBase}/_apis/git/repositories/auditvare-uat-backend"), RepositoryJson)
            .OnJson(PathIs($"{ProjectBase}/_apis/git/repositories/repo-1/refs"), "{\"count\":1,\"value\":[{\"name\":\"refs/heads/main-old\"}]}");

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).GetLatestSuccessfulBuildAsync(Credentials, "auditvare-uat-backend", "main", null, null));

        Assert.Equal(AzureDevOpsErrorCode.BranchNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task LatestBuild_UnknownRepository_ThrowsRepositoryNotFound()
    {
        var handler = new FakeHttpMessageHandler().OnJson(PathIs(ProjectPath), ProjectJson);

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).GetLatestSuccessfulBuildAsync(Credentials, "does-not-exist", "main", null, null));

        Assert.Equal(AzureDevOpsErrorCode.RepositoryNotFound, ex.ErrorCode);
    }

    [Fact]
    public async Task EligibleBuild_BuildFromOtherPipeline_ThrowsBuildNotEligible()
    {
        var handler = BaseRoutes().OnJson(PathIs($"{ProjectBase}/_apis/build/builds/10"), Build(10, pipelineId: 59));

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).GetEligibleBuildAsync(Credentials, 10, "auditvare-uat-backend", "main", 36));

        Assert.Equal(AzureDevOpsErrorCode.BuildNotEligible, ex.ErrorCode);
    }

    [Fact]
    public async Task Download_WritesArtifactZip()
    {
        var content = Encoding.UTF8.GetBytes("zip-bytes");
        var handler = new FakeHttpMessageHandler()
            .On(r => r.RequestUri!.Query.Contains("format=zip"), _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            })
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"), Artifact("drop"));

        var destination = Path.Combine(_tempDirectory, "artifact.zip");
        await CreateService(handler).DownloadArtifactAsync(Credentials, 5, "drop", destination);

        Assert.Equal(content, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task Download_UntrustedDownloadHost_IsRejectedWithoutSendingToken()
    {
        var handler = new FakeHttpMessageHandler()
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"),
                Artifact("drop", downloadUrl: "https://attacker.example.com/drop.zip"));

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).DownloadArtifactAsync(Credentials, 5, "drop", Path.Combine(_tempDirectory, "artifact.zip")));

        Assert.Equal(AzureDevOpsErrorCode.ArtifactNotDownloadable, ex.ErrorCode);
        Assert.DoesNotContain(handler.Requests, r => r.RequestUri!.Host == "attacker.example.com");
    }

    [Fact]
    public async Task Download_FileShareArtifact_IsNotDownloadable()
    {
        var handler = new FakeHttpMessageHandler()
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"), Artifact("drop", type: "FilePath"));

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).DownloadArtifactAsync(Credentials, 5, "drop", Path.Combine(_tempDirectory, "artifact.zip")));

        Assert.Equal(AzureDevOpsErrorCode.ArtifactNotDownloadable, ex.ErrorCode);
    }

    [Fact]
    public async Task Download_AboveSizeLimit_ThrowsAndRemovesPartialFile()
    {
        var handler = new FakeHttpMessageHandler()
            .On(r => r.RequestUri!.Query.Contains("format=zip"), _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[2 * 1024 * 1024])
            })
            .OnJson(PathIs($"{ProjectBase}/_apis/build/builds/5/artifacts"), Artifact("drop"));

        var destination = Path.Combine(_tempDirectory, "artifact.zip");
        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler, new AzureDevOpsOptions { MaxArtifactSizeMB = 1 }).DownloadArtifactAsync(Credentials, 5, "drop", destination));

        Assert.Equal(AzureDevOpsErrorCode.ArtifactTooLarge, ex.ErrorCode);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task Download_ArtifactMissing_ThrowsArtifactNotFound()
    {
        var handler = new FakeHttpMessageHandler();

        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(() =>
            CreateService(handler).DownloadArtifactAsync(Credentials, 5, "drop", Path.Combine(_tempDirectory, "artifact.zip")));

        Assert.Equal(AzureDevOpsErrorCode.ArtifactNotFound, ex.ErrorCode);
    }

    private static AzureDevOpsService CreateService(FakeHttpMessageHandler handler, AzureDevOpsOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? new AzureDevOpsOptions()), NullLogger<AzureDevOpsService>.Instance);

    private static FakeHttpMessageHandler BaseRoutes() => new FakeHttpMessageHandler()
        .OnJson(PathIs(ProjectPath), ProjectJson)
        .OnJson(PathIs($"{ProjectBase}/_apis/git/repositories/auditvare-uat-backend"), RepositoryJson)
        .OnJson(PathIs($"{ProjectBase}/_apis/git/repositories/repo-1/refs"), "{\"count\":1,\"value\":[{\"name\":\"refs/heads/main\"}]}");

    private static Func<HttpRequestMessage, bool> PathIs(string path) =>
        request => request.RequestUri!.AbsolutePath == path && !request.RequestUri.Query.Contains("format=zip");

    private const string ProjectJson = "{\"id\":\"project-1\",\"name\":\"AuditVare_Release_2\"}";
    private const string RepositoryJson = "{\"id\":\"repo-1\",\"name\":\"auditvare-uat-backend\"}";

    private static string Builds(params string[] builds) => $"{{\"count\":{builds.Length},\"value\":[{string.Join(",", builds)}]}}";

    private static string Build(int id, int pipelineId = 36, string branch = "refs/heads/main", string result = "succeeded",
        string repositoryId = "repo-1", string projectId = "project-1") =>
        JsonSerializer.Serialize(new
        {
            id,
            buildNumber = $"2026.{id}",
            status = "completed",
            result,
            sourceBranch = branch,
            sourceVersion = "abcdef1234567890",
            finishTime = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc).AddDays(id),
            project = new { id = projectId },
            repository = new { id = repositoryId },
            definition = new { id = pipelineId, name = $"pipeline-{pipelineId}" }
        });

    private static string Artifacts(params string[] artifacts) => $"{{\"count\":{artifacts.Length},\"value\":[{string.Join(",", artifacts)}]}}";

    private static string Artifact(string name, string type = "Container", string? downloadUrl = null) =>
        JsonSerializer.Serialize(new
        {
            name,
            resource = new
            {
                type,
                downloadUrl = downloadUrl
                    ?? $"https://dev.azure.com/Transviti/project-1/_apis/build/builds/5/artifacts?artifactName={name}&api-version=7.1&%24format=zip",
                properties = new { artifactsize = "1024" }
            }
        });
}
