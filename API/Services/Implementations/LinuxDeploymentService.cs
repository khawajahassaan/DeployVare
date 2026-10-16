using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using API.Data;
using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models;
using API.Models.Entities;
using API.Models.Enums;
using API.Services.Interfaces;
using API.Utilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Renci.SshNet;
using SshConnectionInfo = Renci.SshNet.ConnectionInfo;

namespace API.Services.Implementations;

public class LinuxDeploymentService : ILinuxDeploymentService
{
    private static readonly Regex HostNamePattern = new(
        @"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$",
        RegexOptions.Compiled);
    private static readonly Regex PathPattern = new(@"^/[A-Za-z0-9._/-]+$", RegexOptions.Compiled);
    private static readonly Regex NamePattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$", RegexOptions.Compiled);
    private static readonly Regex UpstreamPattern = new(@"^https?://[A-Za-z0-9.:\[\]-]+/?$", RegexOptions.Compiled);
    private static readonly Regex ConfigJsObject = new(@"=\s*(\{[\s\S]*\})\s*;?\s*$", RegexOptions.Compiled);

    private static readonly TimeSpan DeployScriptTimeout = TimeSpan.FromMinutes(30);
    private const int MaxOutputLines = 2000;

    private readonly DeployVareDbContext _context;
    private readonly IAzureDevOpsService _azureDevOpsService;
    private readonly IAzureDevOpsConfigService _azureDevOpsConfigService;
    private readonly IDeploymentProgressTracker _progress;
    private readonly ILogger<LinuxDeploymentService> _logger;

    public LinuxDeploymentService(
        DeployVareDbContext context,
        IAzureDevOpsService azureDevOpsService,
        IAzureDevOpsConfigService azureDevOpsConfigService,
        IDeploymentProgressTracker progress,
        ILogger<LinuxDeploymentService> logger)
    {
        _context = context;
        _azureDevOpsService = azureDevOpsService;
        _azureDevOpsConfigService = azureDevOpsConfigService;
        _progress = progress;
        _logger = logger;
    }

    // ---- Builds ----------------------------------------------------------------------------------------------------

    public async Task<LinuxBuildsDto> GetBuildsAsync(int applicationId, ArtifactEnvironment environment, CancellationToken cancellationToken = default)
    {
        var application = await _context.Applications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken)
            ?? throw new InvalidOperationException("Application not found.");

        return new LinuxBuildsDto
        {
            ApplicationName = application.Name,
            Slug = LinuxKitBuilder.Slug(application.Name),
            Api = await DescribeBuildAsync(applicationId, ServerType.Api, environment, cancellationToken),
            Frontend = await DescribeBuildAsync(applicationId, ServerType.Frontend, environment, cancellationToken)
        };
    }

    private async Task<LinuxBuildOptionDto> DescribeBuildAsync(int applicationId, ServerType serverType, ArtifactEnvironment environment,
        CancellationToken cancellationToken)
    {
        var option = new LinuxBuildOptionDto { ServerType = serverType.ToString() };
        try
        {
            var tier = await ResolveTierBuildAsync(applicationId, serverType, environment, null, null, cancellationToken);
            option.Build = tier.Build;
            option.ConfiguredArtifactName = tier.Source.ArtifactName;
            option.SuggestedArtifactName = tier.Artifact.Name;
            option.SuggestedIsLinuxSpecific = tier.Artifact.IsLinuxSpecific;
            option.Note = tier.Artifact.Note;
            option.RunDatabaseMigrations = tier.Source.RunDatabaseMigrations;
        }
        catch (AzureDevOpsException ex)
        {
            option.Error = ex.Message;
        }
        return option;
    }

    private sealed record TierBuild(
        ServerType ServerType,
        ArtifactSource Source,
        AzureDevOpsCredentials Credentials,
        AzureBuildDto Build,
        LinuxBuildInspector.ArtifactChoice Artifact);

    private async Task<TierBuild> ResolveTierBuildAsync(int applicationId, ServerType serverType, ArtifactEnvironment environment,
        int? buildId, string? artifactName, CancellationToken cancellationToken)
    {
        var (source, credentials) = await _azureDevOpsConfigService.ResolveSourceAsync(applicationId, serverType, environment, cancellationToken);
        var build = buildId is int id
            ? await _azureDevOpsService.GetEligibleBuildAsync(credentials, id, source.Repository, source.Branch,
                source.PipelineDefinitionId, cancellationToken)
            : await _azureDevOpsService.GetLatestSuccessfulBuildAsync(credentials, source.Repository, source.Branch,
                source.PipelineDefinitionId, string.IsNullOrWhiteSpace(artifactName) ? null : artifactName.Trim(), cancellationToken);
        var artifact = LinuxBuildInspector.SelectArtifact(build.Artifacts, source.ArtifactName, artifactName, serverType);
        return new TierBuild(serverType, source, credentials, build, artifact);
    }

    // ---- Settings --------------------------------------------------------------------------------------------------

    /// <summary>Validated settings of a request, with defaults filled in.</summary>
    private sealed class Plan
    {
        public required string ApplicationName { get; init; }
        public required string Slug { get; init; }
        public bool HasApi { get; init; }
        public bool HasFrontend { get; init; }
        public string ApiPath { get; init; } = string.Empty;
        public string FrontendPath { get; init; } = string.Empty;
        public string ServiceName { get; init; } = string.Empty;
        public string WebServer { get; init; } = "nginx";
        public string? ApiHost { get; init; }
        public string? FrontendHost { get; init; }
        public string? ApiCertificate { get; init; }
        public string? ApiCertificateKey { get; init; }
        public string? FrontendCertificate { get; init; }
        public string? FrontendCertificateKey { get; init; }
        public string? ProxyPath { get; init; }
        public string? ProxyUpstream { get; init; }
        public string? FrontendApiUrl { get; init; }
        public string ApiSiteName => $"{Slug}-api";
        public string FrontendSiteName => $"{Slug}-web";
    }

    private async Task<Plan> BuildPlanAsync(LinuxPackageRequest request, bool? apiAndFrontendOnOneServer, CancellationToken cancellationToken)
    {
        var applicationName = await _context.Applications.Where(a => a.Id == request.ApplicationId).Select(a => a.Name)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Application not found.");
        var slug = LinuxKitBuilder.Slug(applicationName);

        var hasApi = request.Target != LinuxDeploymentTarget.Frontend;
        var hasFrontend = request.Target != LinuxDeploymentTarget.Backend;

        var webServer = (request.WebServer ?? "nginx").Trim().ToLowerInvariant();
        if (webServer is not ("nginx" or "apache"))
            throw new InvalidOperationException("Choose nginx or apache as the web server.");

        var apiHost = Host(request.ApiHostName, "API host name");
        var frontendHost = Host(request.FrontendHostName, "Frontend host name");
        if (hasFrontend && frontendHost == null)
            throw new InvalidOperationException("Enter the frontend host name: the domain (e.g. app.client.com) or the server's IP address.");

        var (apiCert, apiKey) = CertificatePair(request.ApiCertificatePath, request.ApiCertificateKeyPath, "API");
        var (frontCert, frontKey) = CertificatePair(request.FrontendCertificatePath, request.FrontendCertificateKeyPath, "frontend");

        string? proxyPath = null, proxyUpstream = null;
        if (hasFrontend && request.ProxyApiThroughFrontend)
        {
            proxyPath = string.IsNullOrWhiteSpace(request.ApiProxyPath) ? "/api" : "/" + request.ApiProxyPath.Trim().Trim('/');
            if (!PathPattern.IsMatch(proxyPath))
                throw new InvalidOperationException("The API route must look like /api.");

            proxyUpstream = request.ApiProxyUpstream?.Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(proxyUpstream))
            {
                if (hasApi && (apiAndFrontendOnOneServer ?? apiHost == null))
                    proxyUpstream = $"http://127.0.0.1:{request.ApiPort}";
                else if (apiHost != null)
                    proxyUpstream = $"{(apiCert != null ? "https" : "http")}://{apiHost}";
                else
                    throw new InvalidOperationException(
                        "Enter where the frontend's API route goes (e.g. http://10.0.0.5), or the API host name.");
            }
            if (!UpstreamPattern.IsMatch(proxyUpstream))
                throw new InvalidOperationException("The API route target must look like http://127.0.0.1:5000 or https://api.client.com.");
        }

        if (hasApi && apiHost == null && proxyUpstream == null)
            throw new InvalidOperationException(hasFrontend
                ? "Enter the API host name, or let the frontend route /api to the backend."
                : "Enter the API host name: the domain (e.g. api.client.com) or the server's IP address.");
        if (hasApi && hasFrontend && apiHost == null && apiAndFrontendOnOneServer == false)
            throw new InvalidOperationException("The API and frontend are on different servers, so the API needs a host name the frontend server can reach.");

        string? frontendApiUrl = null;
        if (hasFrontend && !string.IsNullOrWhiteSpace(request.FrontendApiUrl))
        {
            frontendApiUrl = request.FrontendApiUrl.Trim();
            if (!Uri.TryCreate(frontendApiUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("The frontend's API URL must start with http:// or https://.");
        }

        var serviceName = string.IsNullOrWhiteSpace(request.ServiceName) ? $"{slug}-api" : request.ServiceName.Trim();
        if (!NamePattern.IsMatch(serviceName))
            throw new InvalidOperationException("The service name may only contain letters, digits, '.', '_' and '-'.");

        return new Plan
        {
            ApplicationName = applicationName,
            Slug = slug,
            HasApi = hasApi,
            HasFrontend = hasFrontend,
            ApiPath = DeploymentPath(request.ApiDeploymentPath, $"/var/www/{slug}-api", "API folder"),
            FrontendPath = DeploymentPath(request.FrontendDeploymentPath, $"/var/www/{slug}-web", "Frontend folder"),
            ServiceName = serviceName,
            WebServer = webServer,
            ApiHost = apiHost,
            FrontendHost = frontendHost,
            ApiCertificate = apiCert,
            ApiCertificateKey = apiKey,
            FrontendCertificate = frontCert,
            FrontendCertificateKey = frontKey,
            ProxyPath = proxyPath,
            ProxyUpstream = proxyUpstream,
            FrontendApiUrl = frontendApiUrl
        };
    }

    private static string? Host(string? value, string label)
    {
        var host = value?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(host)) return null;
        if (!HostNamePattern.IsMatch(host))
            throw new InvalidOperationException($"{label}: enter a domain such as app.client.com or an IP address (no http:// or path).");
        return host;
    }

    private static string DeploymentPath(string? value, string fallback, string label)
    {
        var path = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().TrimEnd('/');
        if (!PathPattern.IsMatch(path) || path.Contains("..") || path.Contains("//") || path.Count(c => c == '/') < 2)
            throw new InvalidOperationException($"{label} must be an absolute folder of its own, such as /var/www/myapp.");
        return path;
    }

    private static (string?, string?) CertificatePair(string? certificate, string? key, string label)
    {
        var cert = string.IsNullOrWhiteSpace(certificate) ? null : certificate.Trim();
        var certKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        if ((cert == null) != (certKey == null))
            throw new InvalidOperationException($"Enter both the {label} certificate and key paths, or neither (HTTP only).");
        foreach (var path in new[] { cert, certKey }.OfType<string>())
            if (!PathPattern.IsMatch(path) || path.Contains(".."))
                throw new InvalidOperationException($"The {label} certificate paths must be absolute paths on the server.");
        return (cert, certKey);
    }

    // ---- Kits ------------------------------------------------------------------------------------------------------

    private sealed record Kit(
        string Tier,
        string Directory,
        LinuxDeployConfig Config,
        IReadOnlyCollection<string> Executables,
        string BuildNumber,
        string ArtifactName,
        string? Note);

    /// <summary>Stage calls with a tier prefix ("api-", "frontend-").</summary>
    private sealed class Stages(IDeploymentProgressTracker tracker, string? progressId)
    {
        public void Begin(string key) => tracker.Begin(progressId, key);
        public void Report(string key, string detail) => tracker.Report(progressId, key, detail);
        public void Done(string key, string? detail = null) => tracker.Complete(progressId, key, DeploymentStageState.Succeeded, detail);
        public void Warn(string key, string detail) => tracker.Complete(progressId, key, DeploymentStageState.Warning, detail);
        public void Skip(string key, string detail) => tracker.Complete(progressId, key, DeploymentStageState.Skipped, detail);

        public Action<long, long?> Download(string key) => (done, total) =>
            Report(key, total is long t && t > 0 ? $"{Size(done)} of {Size(t)} ({done * 100 / t}%)" : $"{Size(done)} downloaded");
    }

    private async Task<Kit> PrepareApiKitAsync(TierBuild tier, Plan plan, LinuxPackageRequest request, string workspace,
        string connectionString, bool runMigrations, int tierCount, Stages stages, CancellationToken cancellationToken)
    {
        var source = await DownloadTierAsync(tier, workspace, "api", stages, cancellationToken);

        stages.Begin("api-kit");
        var entry = LinuxBuildInspector.FindBackendEntry(source.ContentDirectory);
        var kitDirectory = Path.Combine(workspace, "kit-api");
        var appDirectory = Path.Combine(kitDirectory, LinuxKitBuilder.AppFolder);
        LinuxKitBuilder.CopyDirectory(source.ContentDirectory, appDirectory);

        var scriptsDirectory = await DownloadScriptsAsync(tier, workspace, stages, cancellationToken);
        if (scriptsDirectory != null)
        {
            LinuxKitBuilder.CopyDirectory(scriptsDirectory, Path.Combine(kitDirectory, LinuxKitBuilder.ScriptsFolder));
            LinuxKitBuilder.EnsureDbMigratorRuntimeConfig(appDirectory, appDirectory);
        }

        var appSettings = AppSettingsTemplate.Ensure(appDirectory, connectionString);
        var keepPaths = KeepOnServerPaths.Parse(tier.Source.KeepOnServerPaths);
        if (appSettings.Generated && !keepPaths.Contains(AppSettingsTemplate.FileName, StringComparer.OrdinalIgnoreCase))
            keepPaths.Add(AppSettingsTemplate.FileName);

        var config = new LinuxDeployConfig
        {
            KitType = "Api",
            ApplicationName = plan.ApplicationName,
            BuildNumber = tier.Build.BuildNumber,
            PackageSetId = request.ProgressId,
            SiteName = plan.ApiSiteName,
            SiteHostName = plan.ApiHost,
            DeploymentPath = plan.ApiPath,
            WebServer = plan.WebServer,
            CertificatePath = plan.ApiCertificate,
            CertificateKeyPath = plan.ApiCertificateKey,
            ServiceName = plan.ServiceName,
            AppEntry = entry.EntryFile,
            SelfContained = entry.SelfContained,
            AppPort = request.ApiPort,
            RequiredDotNetMajor = entry.DotNetMajor,
            InstallRedis = request.InstallRedis,
            AppSettingsGenerated = appSettings.Generated,
            KeepOnServerPaths = keepPaths,
            RunMigrations = runMigrations && scriptsDirectory != null,
            HasScripts = scriptsDirectory != null
        };

        var notes = new[]
        {
            tier.Artifact.Note,
            appSettings.Generated ? $"The build has no appsettings.json; a template was added ({appSettings.BasedOn ?? "minimal layout"})." : null
        }.OfType<string>().ToList();
        var fileName = LinuxKitBuilder.BuildFileName(plan.ApplicationName, "api", tier.Build.BuildNumber);
        LinuxKitBuilder.WriteKitFiles(kitDirectory, config,
            LinuxKitBuilder.BuildReadme(config, fileName, tierCount, notes.Count > 0 ? string.Join(" ", notes) : null));

        stages.Done("api-kit", $"{(entry.SelfContained ? entry.EntryFile + " (self-contained)" : "dotnet " + entry.EntryFile)}"
            + (appSettings.Generated ? ", appsettings.json template added" : ", appsettings.json from the build")
            + (scriptsDirectory != null ? ", SQL scripts" : ""));

        var executables = entry.SelfContained ? new[] { $"{LinuxKitBuilder.AppFolder}/{entry.EntryFile}" } : Array.Empty<string>();
        return new Kit("api", kitDirectory, config, executables, tier.Build.BuildNumber, tier.Artifact.Name, notes.FirstOrDefault());
    }

    private async Task<Kit> PrepareFrontendKitAsync(TierBuild tier, Plan plan, LinuxPackageRequest request, string workspace,
        int tierCount, Stages stages, CancellationToken cancellationToken)
    {
        var source = await DownloadTierAsync(tier, workspace, "frontend", stages, cancellationToken);

        stages.Begin("frontend-kit");
        var kitDirectory = Path.Combine(workspace, "kit-frontend");
        var appDirectory = Path.Combine(kitDirectory, LinuxKitBuilder.AppFolder);
        LinuxKitBuilder.CopyDirectory(source.ContentDirectory, appDirectory);

        var keepPaths = KeepOnServerPaths.Parse(tier.Source.KeepOnServerPaths);
        string configDetail;
        if (plan.FrontendApiUrl != null)
        {
            WriteConfigJs(appDirectory, plan, plan.FrontendApiUrl);
            configDetail = $"config.js with VITE_API_URL={plan.FrontendApiUrl}";
        }
        else
        {
            // Without an API URL the server's config.js (edited by hand) survives redeployments
            if (!keepPaths.Contains("config.js", StringComparer.OrdinalIgnoreCase)) keepPaths.Add("config.js");
            configDetail = "the build's config.js (kept on the server when it exists)";
        }

        var config = new LinuxDeployConfig
        {
            KitType = "Frontend",
            ApplicationName = plan.ApplicationName,
            BuildNumber = tier.Build.BuildNumber,
            PackageSetId = request.ProgressId,
            SiteName = plan.FrontendSiteName,
            SiteHostName = plan.FrontendHost,
            DeploymentPath = plan.FrontendPath,
            WebServer = plan.WebServer,
            CertificatePath = plan.FrontendCertificate,
            CertificateKeyPath = plan.FrontendCertificateKey,
            KeepOnServerPaths = keepPaths,
            ApiProxyPath = plan.ProxyPath,
            ApiProxyUpstream = plan.ProxyUpstream
        };

        var fileName = LinuxKitBuilder.BuildFileName(plan.ApplicationName, "frontend", tier.Build.BuildNumber);
        LinuxKitBuilder.WriteKitFiles(kitDirectory, config, LinuxKitBuilder.BuildReadme(config, fileName, tierCount, tier.Artifact.Note));
        stages.Done("frontend-kit", $"{source.FileCount} file(s), {configDetail}");
        return new Kit("frontend", kitDirectory, config, Array.Empty<string>(), tier.Build.BuildNumber, tier.Artifact.Name, tier.Artifact.Note);
    }

    /// <summary>Three-tier manual download: DbMigrator and the SQL scripts from the API kit, with their own appsettings.json.</summary>
    private static Kit? PrepareDatabaseKit(Kit apiKit, Plan plan, string connectionString, string workspace, Stages stages)
    {
        stages.Begin("database-kit");
        var apiScripts = Path.Combine(apiKit.Directory, LinuxKitBuilder.ScriptsFolder);
        if (!Directory.Exists(apiScripts))
        {
            stages.Skip("database-kit", "This build has no SQL scripts, so there is no database kit");
            return null;
        }

        var kitDirectory = Path.Combine(workspace, "kit-database");
        if (!LinuxKitBuilder.CopyDbMigrator(Path.Combine(apiKit.Directory, LinuxKitBuilder.AppFolder),
                Path.Combine(kitDirectory, LinuxKitBuilder.DbMigratorFolder)))
            throw new InvalidOperationException("DbMigrator.dll was not found in the API build, so the database kit cannot run the SQL scripts.");

        // The scripts move out of the API kit, which then never migrates
        Directory.Move(apiScripts, Path.Combine(kitDirectory, LinuxKitBuilder.ScriptsFolder));
        apiKit.Config.HasScripts = false;
        apiKit.Config.RunMigrations = false;
        File.WriteAllText(Path.Combine(apiKit.Directory, LinuxKitBuilder.ConfigName),
            System.Text.Json.JsonSerializer.Serialize(apiKit.Config, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }) + "\n", new UTF8Encoding(false));

        AppSettingsTemplate.Ensure(kitDirectory, connectionString);
        var config = new LinuxDeployConfig
        {
            KitType = "Database",
            ApplicationName = plan.ApplicationName,
            BuildNumber = apiKit.BuildNumber,
            PackageSetId = apiKit.Config.PackageSetId,
            SiteName = $"{plan.Slug}-database",
            RequiredDotNetMajor = apiKit.Config.RequiredDotNetMajor,
            AppSettingsGenerated = true,
            RunMigrations = true,
            HasScripts = true,
            DbMigratorFolder = LinuxKitBuilder.DbMigratorFolder
        };
        var fileName = LinuxKitBuilder.BuildFileName(plan.ApplicationName, "database", apiKit.BuildNumber);
        LinuxKitBuilder.WriteKitFiles(kitDirectory, config, LinuxKitBuilder.BuildReadme(config, fileName, 3, null));

        var scriptCount = Directory.GetFiles(Path.Combine(kitDirectory, LinuxKitBuilder.ScriptsFolder), "*.sql", SearchOption.AllDirectories).Length;
        stages.Done("database-kit", $"DbMigrator and {scriptCount} SQL script(s)");
        return new Kit("database", kitDirectory, config, Array.Empty<string>(), apiKit.BuildNumber, apiKit.ArtifactName, null);
    }

    private sealed record DownloadedTier(string ContentDirectory, int FileCount);

    private async Task<DownloadedTier> DownloadTierAsync(TierBuild tier, string workspace, string prefix, Stages stages,
        CancellationToken cancellationToken)
    {
        var key = $"{prefix}-download";
        stages.Begin(key);
        var tierWorkspace = Path.Combine(workspace, $"{prefix}-source");
        Directory.CreateDirectory(tierWorkspace);
        var zipPath = Path.Combine(tierWorkspace, "artifact.zip");
        stages.Report(key, $"'{tier.Artifact.Name}' from build #{tier.Build.BuildNumber}");
        await _azureDevOpsService.DownloadArtifactAsync(tier.Credentials, tier.Build.Id, tier.Artifact.Name, zipPath, cancellationToken,
            stages.Download(key));

        var package = await Task.Run(() => ArtifactPackageBuilder.Build(zipPath, tierWorkspace, tier.Artifact.Name, tier.ServerType,
            tier.Source.ArtifactSubfolder), cancellationToken);
        var detail = $"'{tier.Artifact.Name}' #{tier.Build.BuildNumber}, {package.Files.Count} file(s)";
        if (tier.Artifact.Note != null || package.Warnings.Count > 0)
            stages.Warn(key, detail + ". " + string.Join(" ", new[] { tier.Artifact.Note }.Concat(package.Warnings).OfType<string>()));
        else
            stages.Done(key, detail);
        return new DownloadedTier(package.ContentDirectory, package.Files.Count);
    }

    private async Task<string?> DownloadScriptsAsync(TierBuild tier, string workspace, Stages stages, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(tier.Source.SqlScriptsArtifactName) ? "sqlscripts" : tier.Source.SqlScriptsArtifactName;
        var artifact = tier.Build.Artifacts.FirstOrDefault(a => a.IsDownloadable && a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (artifact == null) return null;

        stages.Report("api-kit", $"Downloading the SQL scripts ('{artifact.Name}')");
        var scriptsWorkspace = Path.Combine(workspace, "scripts-source");
        Directory.CreateDirectory(scriptsWorkspace);
        var zipPath = Path.Combine(scriptsWorkspace, "sqlscripts.zip");
        await _azureDevOpsService.DownloadArtifactAsync(tier.Credentials, tier.Build.Id, artifact.Name, zipPath, cancellationToken);
        var scripts = await Task.Run(() => ArtifactPackageBuilder.BuildScriptsPackage(zipPath, scriptsWorkspace, artifact.Name), cancellationToken);
        return scripts.ContentDirectory;
    }

    /// <summary>config.js with VITE_API_URL, keeping the other settings of the build's config.js.</summary>
    private static void WriteConfigJs(string appDirectory, Plan plan, string apiUrl)
    {
        var path = Path.Combine(appDirectory, "config.js");
        var settings = new List<RuntimeSettingDto>();
        if (File.Exists(path))
        {
            var match = ConfigJsObject.Match(File.ReadAllText(path));
            if (match.Success)
            {
                try
                {
                    if (JsonNode.Parse(match.Groups[1].Value) is JsonObject existing)
                        settings.AddRange(existing
                            .Where(p => p.Value is JsonValue && p.Key != "VITE_API_URL")
                            .Select(p => new RuntimeSettingDto { Key = p.Key, Value = p.Value!.ToString() }));
                }
                catch (System.Text.Json.JsonException)
                {
                    // Not plain JSON: replaced by the generated file
                }
            }
        }
        settings.Insert(0, new RuntimeSettingDto { Key = "VITE_API_URL", Value = apiUrl });
        File.WriteAllText(path, FrontendRuntimeConfig.BuildConfigJs($"Linux {plan.FrontendHost}", settings, DateTime.UtcNow)
            .Replace("Managed in DeployVare (Server Setup > Frontend Runtime Settings)", "Set by DeployVare's Linux deployment"),
            new UTF8Encoding(false));
    }

    private static List<(string Key, string Label)> KitStages(Plan plan, bool databaseKit)
    {
        var stages = new List<(string, string)>();
        if (plan.HasApi)
        {
            stages.Add(("api-download", "API: download the Linux build"));
            stages.Add(("api-kit", "API: assemble the kit (appsettings.json, Deploy.sh, systemd settings)"));
        }
        if (databaseKit) stages.Add(("database-kit", "Database: assemble the kit (DbMigrator, SQL scripts)"));
        if (plan.HasFrontend)
        {
            stages.Add(("frontend-download", "Frontend: download the build"));
            stages.Add(("frontend-kit", "Frontend: assemble the kit (config.js, Deploy.sh, site settings)"));
        }
        return stages;
    }

    // ---- Manual package --------------------------------------------------------------------------------------------

    public async Task<ManualPackageResult> BuildPackageAsync(LinuxPackageRequest request, CancellationToken cancellationToken = default)
    {
        var stages = new Stages(_progress, request.ProgressId);
        var workspace = Path.Combine(Path.GetTempPath(), "DeployVare", "linux-packages", Guid.NewGuid().ToString("N"));
        var databaseKit = request.Target == LinuxDeploymentTarget.ThreeTier;
        Plan plan;
        try
        {
            plan = await BuildPlanAsync(request, apiAndFrontendOnOneServer: null, cancellationToken);
        }
        catch (Exception ex)
        {
            _progress.Start(request.ProgressId, 0, new[] { ("plan", "Check the settings") });
            _progress.Fail(request.ProgressId, ex.Message);
            _progress.Finish(request.ProgressId, succeeded: false);
            throw;
        }

        var stageList = KitStages(plan, databaseKit);
        stageList.Add(("pack", "Create the .tar.gz kits"));
        _progress.Start(request.ProgressId, 0, stageList);
        Directory.CreateDirectory(workspace);

        try
        {
            var tierCount = (plan.HasApi ? 1 : 0) + (plan.HasFrontend ? 1 : 0) + (databaseKit ? 1 : 0);
            var kits = new List<Kit>();
            var connectionString = AppSettingsTemplate.LinuxConnectionString(request.DatabaseServer, request.DatabaseName, request.DatabaseUser);

            if (plan.HasApi)
            {
                var apiTier = await ResolveTierBuildAsync(request.ApplicationId, ServerType.Api, request.Environment,
                    request.ApiBuildId, request.ApiArtifactName, cancellationToken);
                var apiKit = await PrepareApiKitAsync(apiTier, plan, request, workspace, connectionString,
                    runMigrations: request.RunMigrations && !databaseKit, tierCount, stages, cancellationToken);
                if (databaseKit)
                {
                    var database = PrepareDatabaseKit(apiKit, plan, connectionString, workspace, stages);
                    if (database != null) kits.Add(database);
                }
                kits.Add(apiKit);
            }
            if (plan.HasFrontend)
            {
                var frontendTier = await ResolveTierBuildAsync(request.ApplicationId, ServerType.Frontend, request.Environment,
                    request.FrontendBuildId, request.FrontendArtifactName, cancellationToken);
                kits.Add(await PrepareFrontendKitAsync(frontendTier, plan, request, workspace, tierCount, stages, cancellationToken));
            }

            stages.Begin("pack");
            var files = new List<ManualPackageFile>();
            foreach (var kit in kits)
            {
                var fileName = LinuxKitBuilder.BuildFileName(plan.ApplicationName, kit.Tier, kit.BuildNumber);
                var path = Path.Combine(workspace, fileName);
                await Task.Run(() => LinuxKitBuilder.CreateTarGz(kit.Directory, path, kit.Executables,
                    (done, total) => stages.Report("pack", $"{fileName} {(total > 0 ? done * 100 / total : 100)}%")), cancellationToken);
                files.Add(new ManualPackageFile(path, fileName, kit.Tier));
            }
            stages.Done("pack", string.Join(", ", files.Select(f => $"{f.FileName} ({Size(new FileInfo(f.Path).Length)})")));

            // Only the finished kits stay; the folder is deleted when the download expires
            foreach (var directory in Directory.GetDirectories(workspace))
                TryDeleteDirectory(directory);

            _progress.Finish(request.ProgressId, succeeded: true);
            _logger.LogInformation("LinuxPackageReady {Files}", string.Join(", ", files.Select(f => f.FileName)));
            return new ManualPackageResult(files[0].Path, files[0].FileName, 0, 0, Array.Empty<string>()) { Files = files };
        }
        catch (Exception ex)
        {
            _progress.Fail(request.ProgressId, ex is OperationCanceledException ? "Cancelled." : ex.Message);
            _progress.Finish(request.ProgressId, succeeded: false);
            TryDeleteDirectory(workspace);
            throw;
        }
    }

    // ---- Connection test -------------------------------------------------------------------------------------------

    private const string FactsCommand =
        ". /etc/os-release 2>/dev/null; echo \"OS=${PRETTY_NAME:-unknown}\"; " +
        "command -v apt-get >/dev/null 2>&1 && echo APT=1 || echo APT=0; " +
        "{ command -v dnf || command -v yum; } >/dev/null 2>&1 && echo DNF=1 || echo DNF=0; " +
        "command -v getenforce >/dev/null 2>&1 && echo \"SELINUX=$(getenforce)\"; " +
        "command -v systemctl >/dev/null 2>&1 && echo SYSTEMD=1 || echo SYSTEMD=0; " +
        "test -x /usr/sbin/nginx && echo NGINX=1 || echo NGINX=0; " +
        "{ test -x /usr/sbin/apache2 || test -x /usr/sbin/httpd; } && echo APACHE=1 || echo APACHE=0; " +
        "echo \"DOTNET=$(dotnet --list-runtimes 2>/dev/null | awk '/AspNetCore/ {print $2}' | paste -sd, -)\"; " +
        "echo \"DISK=$(df -Pm /var 2>/dev/null | awk 'NR==2 {print $4}')\"";

    public async Task<LinuxConnectionTestDto> TestConnectionAsync(LinuxSshTarget target, CancellationToken cancellationToken = default)
    {
        var result = new LinuxConnectionTestDto();
        try
        {
            ValidateTarget(target, "Server");
            using var client = new SshClient(CreateConnectionInfo(target));
            await client.ConnectAsync(cancellationToken);

            var (_, lines) = await RunAsync(client, FactsCommand, null, TimeSpan.FromSeconds(30), null, cancellationToken);
            var facts = lines.Select(l => l.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1].Trim());
            result.Os = facts.GetValueOrDefault("OS");
            // Ubuntu/Debian (apt) or the Red Hat family (dnf: RHEL, Rocky, AlmaLinux, CentOS Stream)
            result.SupportedOs = (facts.GetValueOrDefault("APT") == "1" || facts.GetValueOrDefault("DNF") == "1")
                && facts.GetValueOrDefault("SYSTEMD") == "1";
            result.SeLinux = facts.GetValueOrDefault("SELINUX");
            result.NginxInstalled = facts.GetValueOrDefault("NGINX") == "1";
            result.ApacheInstalled = facts.GetValueOrDefault("APACHE") == "1";
            result.DotNetRuntimes = string.IsNullOrWhiteSpace(facts.GetValueOrDefault("DOTNET")) ? null : facts["DOTNET"];
            result.DiskFreeMb = long.TryParse(facts.GetValueOrDefault("DISK"), out var disk) ? disk : null;

            var (prefix, stdin) = Sudo(target);
            var (exit, _) = await RunAsync(client, $"{prefix}true", stdin, TimeSpan.FromSeconds(20), null, cancellationToken);
            result.SudoWorks = exit == 0;
            client.Disconnect();

            result.Success = result.SupportedOs && result.SudoWorks == true;
            result.Message = !result.SupportedOs
                ? $"Connected, but {result.Os ?? "this OS"} is not supported: the kits need Ubuntu/Debian or a Red Hat family server (RHEL, Rocky, AlmaLinux) with systemd."
                : result.SudoWorks != true
                    ? SudoHelp(target)
                    : $"Connected to {result.Os} as {target.Username}; sudo works.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result.Success = false;
            result.Message = ex is InvalidOperationException ? ex.Message : SshService.DescribeConnectionFailure(ex, target.Host, target.Port, target.Username);
        }
        return result;
    }

    private static string SudoHelp(LinuxSshTarget target) =>
        string.IsNullOrEmpty(target.SudoPassword ?? target.Password)
            ? $"Connected, but sudo needs a password for {target.Username}. Enter the sudo password, or allow passwordless sudo for this user."
            : $"Connected, but sudo was refused for {target.Username}: check the sudo password and that the user is in the sudo group (Ubuntu) or wheel group (Red Hat).";

    // ---- One-click deployment --------------------------------------------------------------------------------------

    public async Task<LinuxDeploymentResultDto> DeployAsync(LinuxDeployRequest request, int userId, CancellationToken cancellationToken = default)
    {
        var result = new LinuxDeploymentResultDto { StartedAt = DateTime.UtcNow };
        var stages = new Stages(_progress, request.ProgressId);
        var workspace = Path.Combine(Path.GetTempPath(), "DeployVare", "linux-deployments", Guid.NewGuid().ToString("N"));
        var runId = Guid.NewGuid().ToString("N")[..12];

        Plan plan;
        try
        {
            var hasApi = request.Target != LinuxDeploymentTarget.Frontend;
            var hasFrontend = request.Target != LinuxDeploymentTarget.Backend;
            if (hasApi) ValidateTarget(request.ApiServer, "API server");
            if (hasFrontend) ValidateTarget(request.FrontendServer, "Frontend server");
            var oneServer = hasApi && hasFrontend ? SameServer(request.ApiServer!, request.FrontendServer!) : (bool?)null;
            plan = await BuildPlanAsync(request, oneServer, cancellationToken);
        }
        catch (Exception ex)
        {
            _progress.Start(request.ProgressId, 0, new[] { ("plan", "Check the settings") });
            _progress.Fail(request.ProgressId, ex.Message);
            _progress.Finish(request.ProgressId, succeeded: false);
            throw;
        }

        var stageList = new List<(string Key, string Label)> { ("connect", "Connect to the server(s) and check sudo") };
        stageList.AddRange(KitStages(plan, databaseKit: false));
        if (plan.HasApi)
        {
            stageList.Add(("api-upload", $"API: upload the kit to {request.ApiServer!.Host} (SFTP)"));
            stageList.Add(("api-deploy", $"API: run Deploy.sh (files, chmod/chown, systemd {plan.ServiceName}, {plan.WebServer})"));
        }
        if (plan.HasFrontend)
        {
            stageList.Add(("frontend-upload", $"Frontend: upload the kit to {request.FrontendServer!.Host} (SFTP)"));
            stageList.Add(("frontend-deploy", $"Frontend: run Deploy.sh (files, chmod/chown, {plan.WebServer} site)"));
        }
        _progress.Start(request.ProgressId, 0, stageList);
        Directory.CreateDirectory(workspace);

        _logger.LogInformation("LinuxDeploymentStarted {Target} of application {ApplicationId} ({Environment}) by user {UserId}: API {ApiHost}, frontend {FrontendHost}",
            request.Target, request.ApplicationId, request.Environment, userId, request.ApiServer?.Host, request.FrontendServer?.Host);

        try
        {
            // 1. Every server answers and sudo works, before anything is downloaded
            stages.Begin("connect");
            var servers = new List<LinuxSshTarget>();
            if (plan.HasApi) servers.Add(request.ApiServer!);
            if (plan.HasFrontend && !(plan.HasApi && SameServer(request.ApiServer!, request.FrontendServer!))) servers.Add(request.FrontendServer!);
            foreach (var server in servers)
            {
                stages.Report("connect", $"{server.Username}@{server.Host}:{server.Port}");
                var test = await TestConnectionAsync(server, cancellationToken);
                if (!test.Success) throw new InvalidOperationException($"{server.Host}: {test.Message}");
            }
            stages.Done("connect", string.Join(", ", servers.Select(s => s.Host)) + ": supported OS, sudo works");

            // 2. Kits
            var kits = new List<(Kit Kit, LinuxSshTarget Server)>();
            var tierCount = (plan.HasApi ? 1 : 0) + (plan.HasFrontend ? 1 : 0);
            if (plan.HasApi)
            {
                var apiTier = await ResolveTierBuildAsync(request.ApplicationId, ServerType.Api, request.Environment,
                    request.ApiBuildId, request.ApiArtifactName, cancellationToken);
                var apiKit = await PrepareApiKitAsync(apiTier, plan, request, workspace, ConnectionStringFor(request),
                    request.RunMigrations, tierCount, stages, cancellationToken);
                if (apiKit.Config.AppSettingsGenerated && string.IsNullOrEmpty(request.DatabasePassword))
                    await RequireServerAppSettingsAsync(request.ApiServer!, plan, cancellationToken);
                kits.Add((apiKit, request.ApiServer!));
            }
            if (plan.HasFrontend)
            {
                var frontendTier = await ResolveTierBuildAsync(request.ApplicationId, ServerType.Frontend, request.Environment,
                    request.FrontendBuildId, request.FrontendArtifactName, cancellationToken);
                kits.Add((await PrepareFrontendKitAsync(frontendTier, plan, request, workspace, tierCount, stages, cancellationToken),
                    request.FrontendServer!));
            }

            // 3. Upload and run, API first so the frontend's API route has something to reach
            foreach (var (kit, server) in kits)
            {
                var kitResult = new LinuxDeploymentKitResultDto
                {
                    Tier = kit.Tier,
                    Host = server.Host,
                    BuildNumber = kit.BuildNumber,
                    ArtifactName = kit.ArtifactName
                };
                result.Kits.Add(kitResult);
                await DeployKitAsync(kit, server, plan, workspace, runId, kitResult, stages, cancellationToken);
            }

            result.Succeeded = true;
            result.Message = $"{plan.ApplicationName} deployed: " + string.Join(", ", result.Kits.Select(k => $"{k.Tier} #{k.BuildNumber} on {k.Host}"));
            _progress.Finish(request.ProgressId, succeeded: true);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LinuxDeploymentFailed {Target} of application {ApplicationId}", request.Target, request.ApplicationId);
            _progress.Fail(request.ProgressId, ex is OperationCanceledException ? "Cancelled." : ex.Message);
            _progress.Finish(request.ProgressId, succeeded: false);
            throw;
        }
        finally
        {
            result.CompletedAt = DateTime.UtcNow;
            TryDeleteDirectory(workspace);
        }
    }

    private static string ConnectionStringFor(LinuxDeployRequest request)
    {
        if (string.IsNullOrEmpty(request.DatabasePassword))
            return AppSettingsTemplate.LinuxConnectionString(request.DatabaseServer, request.DatabaseName, request.DatabaseUser);

        if (string.IsNullOrWhiteSpace(request.DatabaseServer) || string.IsNullOrWhiteSpace(request.DatabaseName)
            || string.IsNullOrWhiteSpace(request.DatabaseUser))
            throw new InvalidOperationException("With a database password, also enter the SQL Server host, database name and SQL login.");

        var server = request.DatabaseServer.Trim();
        return new SqlConnectionStringBuilder
        {
            DataSource = server.Contains(',') ? server : $"{server},1433",
            InitialCatalog = request.DatabaseName.Trim(),
            UserID = request.DatabaseUser.Trim(),
            Password = request.DatabasePassword,
            TrustServerCertificate = true,
            MultipleActiveResultSets = true
        }.ConnectionString;
    }

    /// <summary>
    /// The build has no appsettings.json and no password was given, so the generated template only has placeholders:
    /// that is fine when the server keeps its own file, otherwise the app could not start.
    /// </summary>
    private async Task RequireServerAppSettingsAsync(LinuxSshTarget server, Plan plan, CancellationToken cancellationToken)
    {
        using var client = new SshClient(CreateConnectionInfo(server));
        await client.ConnectAsync(cancellationToken);
        var (prefix, stdin) = Sudo(server);
        var (exit, _) = await RunAsync(client, $"{prefix}test -f {Quote(plan.ApiPath + "/appsettings.json")}", stdin,
            TimeSpan.FromSeconds(20), null, cancellationToken);
        client.Disconnect();
        if (exit != 0)
            throw new InvalidOperationException(
                $"The build has no appsettings.json and {server.Host} has none in {plan.ApiPath} yet. Enter the database server, name, " +
                "SQL login and password so DeployVare can write one, or use Manual Deployment and edit appsettings.json yourself.");
    }

    private async Task DeployKitAsync(Kit kit, LinuxSshTarget server, Plan plan, string workspace, string runId,
        LinuxDeploymentKitResultDto kitResult, Stages stages, CancellationToken cancellationToken)
    {
        var uploadKey = $"{kit.Tier}-upload";
        var deployKey = $"{kit.Tier}-deploy";
        var fileName = LinuxKitBuilder.BuildFileName(plan.ApplicationName, kit.Tier, kit.BuildNumber);
        var localPath = Path.Combine(workspace, "out", fileName);
        var remoteDirectory = $"/tmp/deployvare-{runId}-{kit.Tier}";

        stages.Begin(uploadKey);
        stages.Report(uploadKey, "Packing the kit");
        await Task.Run(() => LinuxKitBuilder.CreateTarGz(kit.Directory, localPath, kit.Executables), cancellationToken);
        var size = new FileInfo(localPath).Length;

        using var ssh = new SshClient(CreateConnectionInfo(server));
        await ssh.ConnectAsync(cancellationToken);
        var (mkdirExit, mkdirOutput) = await RunAsync(ssh, $"rm -rf {Quote(remoteDirectory)} && mkdir -m 700 {Quote(remoteDirectory)}", null,
            TimeSpan.FromSeconds(20), null, cancellationToken);
        if (mkdirExit != 0)
            throw new InvalidOperationException($"Could not create {remoteDirectory} on {server.Host}: {string.Join(" ", mkdirOutput)}");

        using (var sftp = new SftpClient(CreateConnectionInfo(server)))
        {
            await sftp.ConnectAsync(cancellationToken);
            await using var stream = File.OpenRead(localPath);
            var lastReport = DateTime.MinValue;
            await Task.Run(() => sftp.UploadFile(stream, $"{remoteDirectory}/{fileName}", uploaded =>
            {
                if (DateTime.UtcNow - lastReport < TimeSpan.FromMilliseconds(500) && (long)uploaded < size) return;
                lastReport = DateTime.UtcNow;
                stages.Report(uploadKey, $"{Size((long)uploaded)} of {Size(size)} ({(long)uploaded * 100 / Math.Max(1, size)}%)");
            }), cancellationToken);
            sftp.Disconnect();
        }
        stages.Done(uploadKey, $"{fileName} ({Size(size)}) to {server.Host}:{remoteDirectory}");

        stages.Begin(deployKey);
        var (prefix, stdin) = Sudo(server);
        var folder = LinuxKitBuilder.RootFolderName(fileName);
        var command = $"cd {Quote(remoteDirectory)} && tar -xzf {Quote(fileName)} && cd {Quote(folder)} && {prefix}bash Deploy.sh --yes 2>&1";

        var (exit, output) = await RunAsync(ssh, command, stdin, DeployScriptTimeout, line =>
        {
            if (kitResult.Output.Count < MaxOutputLines) kitResult.Output.Add(line);
            if (line.StartsWith("==> ") || line.StartsWith("[OK]") || line.StartsWith("[WARN]") || line.StartsWith("[FAILED]"))
                stages.Report(deployKey, line);
        }, cancellationToken);

        // The kit (with the app files) is not left in /tmp
        await RunAsync(ssh, $"{prefix}rm -rf {Quote(remoteDirectory)}", stdin, TimeSpan.FromSeconds(60), null, CancellationToken.None);
        ssh.Disconnect();

        kitResult.Succeeded = exit == 0;
        if (exit != 0)
        {
            var reason = output.LastOrDefault(l => l.StartsWith("[FAILED]")) ?? output.LastOrDefault() ?? $"exit code {exit}";
            throw new InvalidOperationException($"{kit.Tier} on {server.Host}: Deploy.sh failed. {reason}");
        }

        var warnings = output.Where(l => l.StartsWith("[WARN]")).ToList();
        var summary = output.FirstOrDefault(l => l.TrimStart().StartsWith("URL:"))?.Trim() ?? "Deploy.sh completed";
        if (warnings.Count > 0) stages.Warn(deployKey, summary + ". " + string.Join(" ", warnings));
        else stages.Done(deployKey, summary);
    }

    // ---- SSH -------------------------------------------------------------------------------------------------------

    private static void ValidateTarget(LinuxSshTarget? target, string label)
    {
        if (target == null || string.IsNullOrWhiteSpace(target.Host) || string.IsNullOrWhiteSpace(target.Username))
            throw new InvalidOperationException($"{label}: enter the host, SSH port and username.");
        if (string.IsNullOrEmpty(target.Password) && string.IsNullOrWhiteSpace(target.PrivateKey))
            throw new InvalidOperationException($"{label}: enter the password or paste the SSH private key.");
        if (Host(target.Host, $"{label} host") == null)
            throw new InvalidOperationException($"{label}: enter the host.");
    }

    private static bool SameServer(LinuxSshTarget a, LinuxSshTarget b) =>
        a.Host.Trim().Equals(b.Host.Trim(), StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;

    private static SshConnectionInfo CreateConnectionInfo(LinuxSshTarget target)
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(target.PrivateKey))
        {
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(target.PrivateKey.Replace("\r\n", "\n").Trim() + "\n"));
            methods.Add(new PrivateKeyAuthenticationMethod(target.Username, new PrivateKeyFile(keyStream)));
        }
        if (!string.IsNullOrEmpty(target.Password))
        {
            methods.Add(new PasswordAuthenticationMethod(target.Username, target.Password));
            // Servers that only offer keyboard-interactive login ask for the same password
            var keyboard = new KeyboardInteractiveAuthenticationMethod(target.Username);
            keyboard.AuthenticationPrompt += (_, e) =>
            {
                foreach (var prompt in e.Prompts)
                    prompt.Response = target.Password;
            };
            methods.Add(keyboard);
        }
        return new SshConnectionInfo(target.Host.Trim(), target.Port, target.Username.Trim(), methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// sudo prefix and its stdin: nothing for root, the password on stdin (never on the command line) when there is
    /// one, otherwise non-interactive sudo (NOPASSWD).
    /// </summary>
    private static (string Prefix, string? Stdin) Sudo(LinuxSshTarget target)
    {
        if (target.Username.Trim() == "root") return ("", null);
        var password = !string.IsNullOrEmpty(target.SudoPassword) ? target.SudoPassword : target.Password;
        return string.IsNullOrEmpty(password) ? ("sudo -n ", null) : ("sudo -S -p '' ", password + "\n");
    }

    /// <summary>Runs a command, streaming each output line; returns the exit code (-1 when unknown) and the lines.</summary>
    private static async Task<(int ExitCode, List<string> Output)> RunAsync(SshClient client, string command, string? stdin,
        TimeSpan timeout, Action<string>? onLine, CancellationToken cancellationToken)
    {
        using var ssh = client.CreateCommand(command);
        ssh.CommandTimeout = timeout;
        var lines = new List<string>();

        var execute = ssh.ExecuteAsync(cancellationToken);
        await using (var input = ssh.CreateInputStream())
        {
            if (stdin != null)
                await input.WriteAsync(Encoding.UTF8.GetBytes(stdin), cancellationToken);
        }

        var reader = Task.Run(async () =>
        {
            using var output = new StreamReader(ssh.OutputStream);
            while (await output.ReadLineAsync(cancellationToken) is { } line)
            {
                lines.Add(line);
                onLine?.Invoke(line);
            }
        }, cancellationToken);

        await execute;
        await reader;
        if (lines.Count == 0 && !string.IsNullOrWhiteSpace(ssh.Error))
            lines.AddRange(ssh.Error.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        return (ssh.ExitStatus ?? -1, lines);
    }

    private static string Quote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static string Size(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.00} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (Exception)
        {
        }
    }
}
