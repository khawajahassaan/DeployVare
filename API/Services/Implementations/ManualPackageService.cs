using System.IO.Compression;
using System.Text;
using System.Text.Json;
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
using Microsoft.Extensions.Options;

namespace API.Services.Implementations;

public class ManualPackageService : IManualPackageService
{
    private static readonly Regex HostNamePattern = new(
        @"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly DeployVareDbContext _context;
    private readonly IAzureDevOpsService _azureDevOpsService;
    private readonly IAzureDevOpsConfigService _azureDevOpsConfigService;
    private readonly ILicensingService _licensingService;
    private readonly IInstallerCatalog _installerCatalog;
    private readonly IServerReadinessService _readinessService;
    private readonly IEncryptionService _encryptionService;
    private readonly ServerSetupOptions _setupOptions;
    private readonly IDeploymentProgressTracker _progress;
    private readonly ILogger<ManualPackageService> _logger;

    public ManualPackageService(
        DeployVareDbContext context,
        IAzureDevOpsService azureDevOpsService,
        IAzureDevOpsConfigService azureDevOpsConfigService,
        ILicensingService licensingService,
        IInstallerCatalog installerCatalog,
        IServerReadinessService readinessService,
        IEncryptionService encryptionService,
        IOptions<ServerSetupOptions> setupOptions,
        IDeploymentProgressTracker progress,
        ILogger<ManualPackageService> logger)
    {
        _context = context;
        _azureDevOpsService = azureDevOpsService;
        _azureDevOpsConfigService = azureDevOpsConfigService;
        _licensingService = licensingService;
        _installerCatalog = installerCatalog;
        _readinessService = readinessService;
        _encryptionService = encryptionService;
        _setupOptions = setupOptions.Value;
        _progress = progress;
        _logger = logger;
    }

    // Live progress of a package build, reported under the client's ProgressId (same tracker as deployments)
    private static readonly (string Key, string Label)[] SingleStages =
    {
        ("download", "Download the build artifact"),
        ("scripts", "Download the SQL scripts"),
        ("kit", "Assemble the package (installers, config, licence)"),
        ("zip", "Create the zip"),
    };

    private static readonly (string Key, string Label)[] CombinedStages =
    {
        ("api-download", "API: download the build artifact"),
        ("api-scripts", "API: download the SQL scripts"),
        ("api-kit", "API: assemble the kit"),
        ("frontend-download", "Frontend: download the build artifact"),
        ("frontend-kit", "Frontend: assemble the kit"),
        ("database-kit", "Database: assemble the kit (DbMigrator, scripts, installers, backup)"),
        ("zip", "Create the three-tier zip"),
    };

    public async Task<ManualPackageResult> BuildAsync(DownloadArtifactPackageRequest request, CancellationToken cancellationToken = default)
    {
        var stages = new StageReporter(_progress, request.ProgressId, "");
        _progress.Start(request.ProgressId, 0, request.Combined ? CombinedStages : SingleStages);
        try
        {
            var result = request.Combined
                ? await BuildCombinedAsync(request, stages, cancellationToken)
                : await BuildSingleAsync(request, certificatePath: null, separateDatabase: false, stages, zipIsOwnStage: true, cancellationToken);
            _progress.Finish(request.ProgressId, succeeded: true);
            return result;
        }
        catch (Exception ex)
        {
            _progress.Fail(request.ProgressId, ex is OperationCanceledException ? "Cancelled: the browser stopped waiting." : ex.Message);
            _progress.Finish(request.ProgressId, succeeded: false);
            throw;
        }
    }

    /// <summary>Stage calls for one kit; the prefix ("api-", "frontend-") maps them onto the three-tier plan.</summary>
    private sealed class StageReporter(IDeploymentProgressTracker tracker, string? progressId, string prefix)
    {
        public StageReporter For(string kitPrefix) => new(tracker, progressId, kitPrefix);
        public void Begin(string key) => tracker.Begin(progressId, prefix + key);
        public void Report(string key, string detail) => tracker.Report(progressId, prefix + key, detail);
        public void Done(string key, string? detail = null) => tracker.Complete(progressId, prefix + key, DeploymentStageState.Succeeded, detail);
        public void Skip(string key, string detail) => tracker.Complete(progressId, prefix + key, DeploymentStageState.Skipped, detail);

        public Action<long, long?> Download(string key) => (done, total) =>
            Report(key, total is long t && t > 0 ? $"{Size(done)} of {Size(t)} ({done * 100 / t}%)" : $"{Size(done)} downloaded");

        public Action<long, long> Zip(string key, string label) => (done, total) =>
            Report(key, $"{label} {(total > 0 ? done * 100 / total : 100)}% ({Size(done)} of {Size(total)})");
    }

    private static string Size(long bytes) =>
        bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.00} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.0} MB"
        : $"{Math.Max(1, bytes / 1024)} KB";

    private async Task<ManualPackageResult> BuildCombinedAsync(DownloadArtifactPackageRequest request, StageReporter stages, CancellationToken cancellationToken)
    {
        var selectedContext = await _azureDevOpsConfigService.ResolveDeploymentContextAsync(
            request.ApplicationServerId, request.Environment, cancellationToken);
        var selected = selectedContext.Server;

        var counterparts = await _context.ApplicationServers.AsNoTracking()
            .Where(s => s.ApplicationId == selected.ApplicationId && s.IsActive && s.ServerType != selected.ServerType)
            .ToListAsync(cancellationToken);
        var counterpart = ManualPackagePairing.Resolve(selected, counterparts, request.CounterpartServerId);

        var apiServer = selected.ServerType == ServerType.Api ? selected : counterpart;
        var frontendServer = selected.ServerType == ServerType.Frontend ? selected : counterpart;

        var selectedHost = await ResolveSiteHostNameAsync(request.SiteHostName, selected.Id, cancellationToken);
        var counterpartHost = await ResolveSiteHostNameAsync(request.CounterpartSiteHostName, counterpart.Id, cancellationToken);
        var apiHost = selected.ServerType == ServerType.Api ? selectedHost : counterpartHost;
        var frontendHost = selected.ServerType == ServerType.Frontend ? selectedHost : counterpartHost;

        string? certificateTemp = null;
        ManualPackageResult? apiPackage = null;
        ManualPackageResult? frontendPackage = null;
        var workspace = Path.Combine(Path.GetTempPath(), "DeployVare", "packages", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);

        try
        {
            if (request.CertificateMode == CertificateMode.UserProvided)
                certificateTemp = await SaveCertificateAsync(request, cancellationToken);

            var (apiBuildId, apiArtifact) = await ResolveBuildAsync(
                apiServer.Id, request, selected.ServerType == ServerType.Api, cancellationToken);
            var (frontendBuildId, frontendArtifact) = await ResolveBuildAsync(
                frontendServer.Id, request, selected.ServerType == ServerType.Frontend, cancellationToken);

            var apiRequest = ForInnerServer(request, apiServer.Id, apiBuildId, apiArtifact, apiHost, isApi: true);
            var frontendRequest = ForInnerServer(request, frontendServer.Id, frontendBuildId, frontendArtifact, frontendHost, isApi: false);

            // Three tiers: the API kit leaves SQL Server, the restore and DbMigrator to the database kit
            apiPackage = await BuildSingleAsync(apiRequest, certificateTemp, separateDatabase: true, stages.For("api-"), zipIsOwnStage: false, cancellationToken);
            frontendPackage = await BuildSingleAsync(frontendRequest, certificateTemp, separateDatabase: false, stages.For("frontend-"), zipIsOwnStage: false, cancellationToken);

            stages.Begin("database-kit");
            stages.Report("database-kit", "Unpacking the API and frontend kits");

            var apiDir = Path.Combine(workspace, ManualPackageBuilder.ApiKitFolder);
            var frontendDir = Path.Combine(workspace, ManualPackageBuilder.FrontendKitFolder);
            ZipFile.ExtractToDirectory(apiPackage.ZipPath, apiDir);
            ZipFile.ExtractToDirectory(frontendPackage.ZipPath, frontendDir);

            var applicationName = selected.Application?.Name ?? selected.ServerName;
            var apiBuildNumber = ExtractBuildNumber(apiPackage.FileName);
            var frontendBuildNumber = ExtractBuildNumber(frontendPackage.FileName);

            var databaseDir = Path.Combine(workspace, ManualPackageBuilder.DatabaseKitFolder);
            stages.Report("database-kit", "Copying DbMigrator, SQL scripts, installers and the baseline backup");
            var (databaseConfig, databaseDetail) = BuildDatabaseKit(apiDir, databaseDir, apiServer, request, apiBuildNumber, apiPackage.SqlFileCount);
            stages.Done("database-kit", databaseDetail);
            var fileName = ManualPackageBuilder.BuildCombinedFileName(applicationName, apiBuildNumber, frontendBuildNumber);
            var zipPath = Path.Combine(workspace, fileName);

            stages.Begin("zip");

            await Task.Run(() => ManualPackageBuilder.BuildCombined(
                zipPath,
                apiDir,
                frontendDir,
                ManualPackageBuilder.BuildCombinedReadme(
                    applicationName,
                    apiServer.ServerName,
                    apiBuildNumber,
                    frontendServer.ServerName,
                    frontendBuildNumber,
                    hasDatabaseKit: true,
                    freshDatabase: databaseConfig.FreshDatabase,
                    databaseName: databaseConfig.DatabaseName),
                ManualPackageBuilder.BuildCombinedDeployScript(),
                databaseDir,
                stages.Zip("zip", "Zipping")), cancellationToken);
            stages.Done("zip", $"{fileName} ({Size(new FileInfo(zipPath).Length)})");

            RemoveIntermediateFiles(workspace, zipPath);

            _logger.LogInformation("CombinedManualPackageReady {FileName} api={Api} frontend={Frontend}",
                fileName, apiPackage.FileName, frontendPackage.FileName);

            return new ManualPackageResult(
                zipPath,
                fileName,
                apiPackage.AppFileCount + frontendPackage.AppFileCount,
                apiPackage.SqlFileCount,
                apiPackage.ContentFolders.Concat(frontendPackage.ContentFolders).ToList());
        }
        catch
        {
            TryDeleteDirectory(workspace);
            throw;
        }
        finally
        {
            if (apiPackage != null)
                TryDeleteDirectory(Path.GetDirectoryName(apiPackage.ZipPath)!);
            if (frontendPackage != null)
                TryDeleteDirectory(Path.GetDirectoryName(frontendPackage.ZipPath)!);
            if (certificateTemp != null)
            {
                try { File.Delete(certificateTemp); } catch (IOException) { }
            }
        }
    }

    private async Task<(int BuildId, string? ArtifactName)> ResolveBuildAsync(
        int serverId, DownloadArtifactPackageRequest request, bool useSelectedBuild, CancellationToken cancellationToken)
    {
        if (useSelectedBuild)
            return (request.BuildId, request.ArtifactName);

        var latest = await _azureDevOpsConfigService.GetLatestBuildAsync(serverId, request.Environment, cancellationToken);
        var artifact = latest.SelectedArtifactName ?? latest.Source.ArtifactName;
        if (string.IsNullOrWhiteSpace(artifact))
        {
            throw new InvalidOperationException(
                "The other server has no default artifact. Open Deploy App for that server, or configure a single artifact name on Azure DevOps.");
        }

        return (latest.Build.Id, artifact);
    }

    private static DownloadArtifactPackageRequest ForInnerServer(
        DownloadArtifactPackageRequest source,
        int serverId,
        int buildId,
        string? artifactName,
        string siteHostName,
        bool isApi)
    {
        return new DownloadArtifactPackageRequest
        {
            ApplicationServerId = serverId,
            Environment = source.Environment,
            BuildId = buildId,
            ArtifactName = artifactName,
            LicensingMode = isApi ? source.LicensingMode : LicensingMode.WithoutLicense,
            LicenseId = isApi ? source.LicenseId : null,
            FreshDatabase = isApi && source.FreshDatabase,
            CertificateMode = source.CertificateMode,
            SiteHostName = siteHostName,
            SqlInstanceName = source.SqlInstanceName,
            DatabaseName = source.DatabaseName,
            IncludeInstallers = source.IncludeInstallers,
            Combined = false
        };
    }

    private static string ExtractBuildNumber(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        const string suffix = "_manual-deploy";
        if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            stem = stem[..^suffix.Length];
        var lastUnderscore = stem.LastIndexOf('_');
        return lastUnderscore >= 0 ? stem[(lastUnderscore + 1)..] : stem;
    }

    private async Task<ManualPackageResult> BuildSingleAsync(
        DownloadArtifactPackageRequest request,
        string? certificatePath,
        bool separateDatabase,
        StageReporter stages,
        bool zipIsOwnStage,
        CancellationToken cancellationToken)
    {
        var context = await _azureDevOpsConfigService.ResolveDeploymentContextAsync(
            request.ApplicationServerId, request.Environment, cancellationToken);

        var server = context.Server;
        var source = context.Source;
        var isFrontend = server.ServerType == ServerType.Frontend;

        var license = await ResolveLicenseAsync(request, server, cancellationToken);
        var siteHostName = await ResolveSiteHostNameAsync(request.SiteHostName, server.Id, cancellationToken);
        var (sqlDataSource, databaseName) = await ResolveDatabaseAsync(request, server, isFrontend, cancellationToken);
        var extraFiles = new List<(string EntryName, string SourcePath)>();
        string? certificateTemp = certificatePath;
        var ownCertificate = false;

        if (request.CertificateMode == CertificateMode.UserProvided)
        {
            if (certificateTemp == null)
            {
                certificateTemp = await SaveCertificateAsync(request, cancellationToken);
                ownCertificate = true;
            }
            extraFiles.Add(($"{ManualPackageBuilder.CertificateFolder}/site.pfx", certificateTemp));
        }

        // With a separate database kit, SQL Express and the baseline backup travel in that kit instead
        var packsDatabase = request.FreshDatabase && !isFrontend && !separateDatabase;
        if (request.IncludeInstallers)
            CollectInstallers(isFrontend, packsDatabase, extraFiles);
        // The baseline backup is data, not an installer: Fresh Database needs it either way
        if (packsDatabase)
            CollectBaselineBackup(extraFiles);

        var build = await _azureDevOpsService.GetEligibleBuildAsync(context.Credentials, request.BuildId,
            source.Repository, source.Branch, source.PipelineDefinitionId, cancellationToken);

        var artifactName = string.IsNullOrWhiteSpace(request.ArtifactName) ? source.ArtifactName : request.ArtifactName;
        if (string.IsNullOrWhiteSpace(artifactName))
            throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotFound, "No artifact name is configured for this source.");

        if (!string.IsNullOrWhiteSpace(source.ArtifactName)
            && !source.ArtifactName.Equals(artifactName, StringComparison.OrdinalIgnoreCase))
        {
            throw new AzureDevOpsException(AzureDevOpsErrorCode.BuildNotEligible,
                $"Only the configured artifact '{source.ArtifactName}' can be downloaded from this source.");
        }

        var artifact = build.Artifacts.FirstOrDefault(a => a.Name.Equals(artifactName, StringComparison.OrdinalIgnoreCase))
            ?? throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotFound,
                $"Build #{build.BuildNumber} has no artifact named '{artifactName}'.");

        if (!artifact.IsDownloadable)
            throw new AzureDevOpsException(AzureDevOpsErrorCode.ArtifactNotDownloadable,
                $"Artifact '{artifact.Name}' (type '{artifact.ResourceType}') cannot be downloaded.");

        var workspace = Path.Combine(Path.GetTempPath(), "DeployVare", "packages", $"{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);

        try
        {
            _logger.LogInformation("ManualPackageStarted build {BuildNumber} artifact {Artifact} for {ServerName}",
                build.BuildNumber, artifact.Name, server.ServerName);

            var artifactZipPath = Path.Combine(workspace, "artifact.zip");
            stages.Begin("download");
            stages.Report("download", $"'{artifact.Name}' from build #{build.BuildNumber}");
            await _azureDevOpsService.DownloadArtifactAsync(context.Credentials, build.Id, artifact.Name, artifactZipPath, cancellationToken,
                stages.Download("download"));
            stages.Done("download", $"'{artifact.Name}' #{build.BuildNumber}, {Size(new FileInfo(artifactZipPath).Length)}");

            var package = await Task.Run(() => ArtifactPackageBuilder.Build(
                artifactZipPath, workspace, artifact.Name, server.ServerType, source.ArtifactSubfolder), cancellationToken);

            var contentFolders = FindContentFolders(package.ContentDirectory, source.ArtifactSubfolder);

            string? scriptsDirectory = null;
            var sqlFileCount = 0;
            if (server.ServerType == ServerType.Api)
            {
                var scriptsArtifactName = string.IsNullOrWhiteSpace(source.SqlScriptsArtifactName) ? "sqlscripts" : source.SqlScriptsArtifactName;
                var scriptsArtifact = build.Artifacts.FirstOrDefault(a =>
                    a.IsDownloadable && a.Name.Equals(scriptsArtifactName, StringComparison.OrdinalIgnoreCase));

                if (scriptsArtifact != null)
                {
                    var scriptsZipPath = Path.Combine(workspace, "sqlscripts-artifact.zip");
                    stages.Begin("scripts");
                    await _azureDevOpsService.DownloadArtifactAsync(context.Credentials, build.Id, scriptsArtifact.Name, scriptsZipPath, cancellationToken,
                        stages.Download("scripts"));

                    var scripts = await Task.Run(() =>
                        ArtifactPackageBuilder.BuildScriptsPackage(scriptsZipPath, workspace, scriptsArtifact.Name), cancellationToken);

                    scriptsDirectory = scripts.ContentDirectory;
                    sqlFileCount = scripts.SqlFileCount;
                    stages.Done("scripts", $"'{scriptsArtifact.Name}', {sqlFileCount} script(s)");
                }
                else
                {
                    stages.Skip("scripts", $"This build has no '{scriptsArtifactName}' artifact");
                }
            }
            else
            {
                stages.Skip("scripts", "Frontend builds have no SQL scripts");
            }

            stages.Begin("kit");
            stages.Report("kit", "Adding installers, configuration and Deploy.ps1");

            if (isFrontend)
                WriteFrontendConfigJs(server, package.ContentDirectory);

            var licenseFile = license == null ? null : await _licensingService.GetLicenseFileAsync(license.Id);
            var keepPaths = KeepOnServerPaths.Parse(source.KeepOnServerPaths);
            var sqlInstance = string.IsNullOrWhiteSpace(request.SqlInstanceName) ? "SQLEXPRESS" : request.SqlInstanceName.Trim();

            var config = new ManualDeployConfig
            {
                ServerName = server.ServerName,
                ServerType = isFrontend ? "Frontend" : "Api",
                WebsiteName = server.WebsiteName,
                AppPoolName = server.AppPoolName,
                DeploymentPath = server.DeploymentPath,
                BackupPath = string.IsNullOrWhiteSpace(server.BackupPath) ? @"C:\DeployVare\Backups" : server.BackupPath,
                SiteHostName = siteHostName,
                RequireSni = true,
                PreserveWebConfig = source.PreserveWebConfig,
                KeepOnServerPaths = keepPaths,
                FreshDatabase = request.FreshDatabase && !isFrontend && !separateDatabase,
                SeparateDatabase = separateDatabase && !isFrontend,
                SqlInstanceName = sqlInstance,
                SqlDataSource = sqlDataSource ?? $@".\{sqlInstance}",
                DatabaseName = databaseName,
                CertificateMode = request.CertificateMode == CertificateMode.UserProvided ? "UserProvided" : "Default",
                CertificateFile = request.CertificateMode == CertificateMode.UserProvided ? $"{ManualPackageBuilder.CertificateFolder}/site.pfx" : null,
                DefaultCertificateName = ServerReadinessEvaluator.DefaultCertificateName,
                LicensingMode = request.LicensingMode.ToString(),
                LicenseFileName = licenseFile?.FileName,
                HasScripts = scriptsDirectory != null,
                IncludesInstallers = request.IncludeInstallers,
                ContentFolders = contentFolders.Select(f => f.Name).ToList(),
                RequiredDotNetMajor = _setupOptions.RequiredDotNetMajorVersion,
                MinimumSdkMajor = _setupOptions.MinimumSdkMajorVersion,
                RequiredRedisMajor = _setupOptions.RequiredRedisMajorVersion,
                RedisPort = 6379
            };

            var readme = ManualPackageBuilder.BuildReadme(config, build.BuildNumber, keepPaths);
            var fileName = ManualPackageBuilder.BuildFileName(server.ServerName, build.BuildNumber);
            var zipPath = Path.Combine(workspace, fileName);
            var deployScript = ManualPackageBuilder.LoadDeployScript();
            var configJson = JsonSerializer.Serialize(config, ConfigJson);

            if (zipIsOwnStage)
            {
                stages.Done("kit", $"{package.Files.Count} app file(s), {extraFiles.Count} installer/extra file(s)");
                stages.Begin("zip");
            }
            var zipStage = zipIsOwnStage ? "zip" : "kit";
            await Task.Run(() => ManualPackageBuilder.Build(
                zipPath,
                package.ContentDirectory,
                scriptsDirectory,
                contentFolders,
                readme,
                licenseFile?.FileName,
                licenseFile?.Content,
                deployScript,
                configJson,
                extraFiles,
                stages.Zip(zipStage, zipIsOwnStage ? "Zipping" : "Packing")), cancellationToken);
            stages.Done(zipStage, zipIsOwnStage
                ? $"{fileName} ({Size(new FileInfo(zipPath).Length)})"
                : $"{package.Files.Count} app file(s), {extraFiles.Count} installer/extra file(s)");

            RemoveIntermediateFiles(workspace, zipPath);

            _logger.LogInformation("ManualPackageReady {FileName}: {AppFiles} app file(s), {SqlFiles} script(s), content folders [{Folders}]",
                fileName, package.Files.Count, sqlFileCount, string.Join(", ", contentFolders.Select(f => f.Name)));

            return new ManualPackageResult(zipPath, fileName, package.Files.Count, sqlFileCount,
                contentFolders.Select(f => f.Name).ToList());
        }
        catch
        {
            TryDeleteDirectory(workspace);
            throw;
        }
        finally
        {
            if (ownCertificate && certificateTemp != null)
            {
                try { File.Delete(certificateTemp); } catch (IOException) { }
            }
        }
    }

    private async Task<string> ResolveSiteHostNameAsync(string? siteHostName, int serverId, CancellationToken cancellationToken)
    {
        var supplied = siteHostName?.Trim().TrimEnd('.').ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(supplied))
        {
            if (!HostNamePattern.IsMatch(supplied))
                throw new InvalidOperationException("Enter a valid site host name such as api.client.com (no http:// or path).");
            return supplied;
        }

        var report = await _readinessService.GetLastReportAsync(serverId);
        var fromBindings = report?.Bindings?
            .Select(b => b.HostName)
            .FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
        if (!string.IsNullOrWhiteSpace(fromBindings) && HostNamePattern.IsMatch(fromBindings.Trim().TrimEnd('.').ToLowerInvariant()))
            return fromBindings.Trim().TrimEnd('.').ToLowerInvariant();

        throw new InvalidOperationException(
            "Enter the HTTPS host name for this server (the Site URL from Server Setup, e.g. api.client.com).");
    }

    private async Task<(string? DataSource, string? DatabaseName)> ResolveDatabaseAsync(
        DownloadArtifactPackageRequest request, ApplicationServer server, bool isFrontend, CancellationToken cancellationToken)
    {
        if (isFrontend) return (null, null);

        string? dataSource = null;
        string? databaseName = string.IsNullOrWhiteSpace(request.DatabaseName) ? null : request.DatabaseName.Trim();

        if (server.DatabaseInstanceId != null)
        {
            var instance = await _context.DatabaseInstances.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == server.DatabaseInstanceId.Value, cancellationToken);
            if (instance != null)
            {
                var builder = new SqlConnectionStringBuilder(_encryptionService.Decrypt(instance.ConnectionStringEncrypted));
                dataSource = builder.DataSource;
                databaseName ??= builder.InitialCatalog;
            }
        }

        if (request.FreshDatabase)
        {
            var instanceName = string.IsNullOrWhiteSpace(request.SqlInstanceName) ? "SQLEXPRESS" : request.SqlInstanceName.Trim();
            dataSource = string.Equals(instanceName, "MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
                ? "."
                : $@".\{instanceName}";
            databaseName ??= "AuditVare";
        }

        return (dataSource, databaseName);
    }

    private void CollectInstallers(bool frontend, bool freshDatabase, List<(string, string)> extraFiles)
    {
        if (frontend)
        {
            AddInstaller(ServerComponent.UrlRewrite, extraFiles, required: true,
                "Frontend packages need rewrite_amd64*.msi in the Installers folder (IIS URL Rewrite).");
            return;
        }

        AddInstaller(ServerComponent.HostingBundle, extraFiles, required: true,
            "Add the .NET 10 Hosting Bundle (dotnet-hosting-*-win.exe) to the Installers folder.");
        AddInstaller(ServerComponent.DotNetSdk, extraFiles, required: false, null);
        AddInstaller(ServerComponent.Redis, extraFiles, required: true,
            "Add Memurai-for-Redis-*.msi to the Installers folder. Memurai installs Redis 8 on port 6379.");
        if (freshDatabase)
        {
            AddInstaller(ServerComponent.SqlServer, extraFiles, required: true,
                "Fresh Database needs SQL2025-SSEI-Expr.exe or SQLEXPR_x64_ENU.exe in the Installers folder.");
        }
    }

    private void AddInstaller(ServerComponent component, List<(string, string)> extraFiles, bool required, string? missingMessage)
    {
        var path = _installerCatalog.GetInstallerPath(component);
        if (path == null)
        {
            if (required)
                throw new InvalidOperationException(missingMessage ?? $"No installer for {component} was found in the Installers folder.");
            return;
        }

        extraFiles.Add(($"{ManualPackageBuilder.InstallersFolder}/{Path.GetFileName(path)}", path));
    }

    private void CollectBaselineBackup(List<(string, string)> extraFiles)
    {
        var backup = FindBaselineBackup();
        extraFiles.Add(($"{ManualPackageBuilder.DatabaseFolder}/{Path.GetFileName(backup)}", backup));
    }

    /// <summary>
    /// Database kit of a three-tier package, assembled next to the extracted API kit: the build's SQL scripts
    /// (moved out of the API kit), DbMigrator, the .NET runtime installer DbMigrator needs, SQL Express and
    /// the baseline backup for Fresh Database, Deploy.ps1 and its config.
    /// </summary>
    private (ManualDeployConfig Config, string Detail) BuildDatabaseKit(
        string apiKitDirectory,
        string databaseKitDirectory,
        ApplicationServer apiServer,
        DownloadArtifactPackageRequest request,
        string buildNumber,
        int sqlFileCount)
    {
        Directory.CreateDirectory(databaseKitDirectory);
        var apiConfigPath = Path.Combine(apiKitDirectory, ManualPackageBuilder.ConfigName);
        var apiConfig = JsonSerializer.Deserialize<ManualDeployConfig>(File.ReadAllText(apiConfigPath), ConfigJson)
            ?? throw new InvalidOperationException("The API kit has no deploy-config.json.");

        var scriptsSource = Path.Combine(apiKitDirectory, ManualPackageBuilder.ScriptsFolder);
        var hasScripts = Directory.Exists(scriptsSource);
        var migratorDetail = "no SQL scripts in this build";
        if (hasScripts)
        {
            Directory.Move(scriptsSource, Path.Combine(databaseKitDirectory, ManualPackageBuilder.ScriptsFolder));

            var migrator = DbMigratorKit.Copy(
                Path.Combine(apiKitDirectory, ManualPackageBuilder.AppFolder),
                Path.Combine(databaseKitDirectory, ManualPackageBuilder.DbMigratorFolder));
            if (!migrator.Found)
                throw new InvalidOperationException(
                    "DbMigrator.exe was not found in the API build, so the database kit cannot run the SQL scripts.");
            _logger.LogInformation("DatabaseKit DbMigrator copied: {Files} file(s), trimmed {Trimmed}", migrator.FileCount, migrator.Trimmed);
            migratorDetail = $"DbMigrator ({migrator.FileCount} files), {sqlFileCount} script(s)";
        }

        // The API kit no longer carries the scripts
        apiConfig.HasScripts = false;
        File.WriteAllText(apiConfigPath, JsonSerializer.Serialize(apiConfig, ConfigJson), new UTF8Encoding(false));

        var installers = new List<(string EntryName, string SourcePath)>();
        if (hasScripts && request.IncludeInstallers)
            AddInstaller(ServerComponent.HostingBundle, installers, required: true,
                "Add the .NET 10 Hosting Bundle (dotnet-hosting-*-win.exe) to the Installers folder; DbMigrator needs the .NET runtime.");
        if (request.FreshDatabase)
        {
            if (request.IncludeInstallers)
                AddInstaller(ServerComponent.SqlServer, installers, required: true,
                    "Fresh Database needs SQL2025-SSEI-Expr.exe or SQLEXPR_x64_ENU.exe in the Installers folder.");
            var backup = FindBaselineBackup();
            installers.Add(($"{ManualPackageBuilder.BackupFolder}/{Path.GetFileName(backup)}", backup));
        }
        foreach (var (entryName, sourcePath) in installers)
        {
            var target = Path.Combine(databaseKitDirectory, entryName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(sourcePath, target, overwrite: true);
        }

        var sqlInstance = string.IsNullOrWhiteSpace(apiConfig.SqlInstanceName) ? "SQLEXPRESS" : apiConfig.SqlInstanceName;
        var config = new ManualDeployConfig
        {
            ServerName = $"{apiServer.ServerName} database",
            ServerType = "Database",
            FreshDatabase = request.FreshDatabase,
            SqlInstanceName = sqlInstance,
            SqlDataSource = string.IsNullOrWhiteSpace(apiConfig.SqlDataSource) ? $@".\{sqlInstance}" : apiConfig.SqlDataSource,
            DatabaseName = string.IsNullOrWhiteSpace(apiConfig.DatabaseName) ? "AuditVare" : apiConfig.DatabaseName,
            HasScripts = hasScripts,
            IncludesInstallers = request.IncludeInstallers,
            DbMigratorFolder = hasScripts ? ManualPackageBuilder.DbMigratorFolder : null,
            RequiredDotNetMajor = _setupOptions.RequiredDotNetMajorVersion,
            MinimumSdkMajor = _setupOptions.MinimumSdkMajorVersion,
            RequiredRedisMajor = _setupOptions.RequiredRedisMajorVersion
        };

        File.WriteAllText(Path.Combine(databaseKitDirectory, ManualPackageBuilder.ConfigName),
            JsonSerializer.Serialize(config, ConfigJson), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(databaseKitDirectory, ManualPackageBuilder.DeployScriptName),
            ManualPackageBuilder.LoadDeployScript(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(databaseKitDirectory, ManualPackageBuilder.ReadmeName),
            ManualPackageBuilder.BuildDatabaseReadme(config, buildNumber, sqlFileCount), new UTF8Encoding(false));

        var detail = migratorDetail + (request.FreshDatabase ? ", SQL Express and the baseline backup" : "");
        return (config, detail);
    }

    private string FindBaselineBackup()
    {
        var directory = _installerCatalog.GetInstallers().FirstOrDefault()?.Directory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            throw new InvalidOperationException("The Installers folder was not found, so the baseline database backup cannot be packaged.");

        var bak = Directory.GetFiles(directory, "*.bak")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (bak != null)
            return bak;

        var zip = Directory.GetFiles(directory, "AuditVareBackup*.zip")
            .Concat(Directory.GetFiles(directory, "*.zip").Where(f => Path.GetFileName(f).Contains("Backup", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (zip == null)
            throw new InvalidOperationException(
                "Fresh Database needs a baseline .bak (or AuditVareBackup*.zip) in the Installers folder.");

        return zip;
    }

    private static async Task<string> SaveCertificateAsync(DownloadArtifactPackageRequest request, CancellationToken cancellationToken)
    {
        var file = request.CertificateFile
            ?? throw new InvalidOperationException("Upload a .pfx certificate, or choose the default certificate option.");
        if (file.Length == 0)
            throw new InvalidOperationException("The certificate file is empty.");
        if (file.Length > 10_000_000)
            throw new InvalidOperationException("The certificate file is larger than 10 MB.");
        var extension = Path.GetExtension(file.FileName);
        if (!extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".p12", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Upload a .pfx or .p12 certificate that includes its private key.");

        var temp = Path.Combine(Path.GetTempPath(), "DeployVare", "certificates", $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}");
        Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
        await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            await file.CopyToAsync(stream, cancellationToken);
        return temp;
    }

    private static void WriteFrontendConfigJs(ApplicationServer server, string appDirectory)
    {
        var settings = FrontendRuntimeConfig.Parse(server.RuntimeConfigJson);
        if (settings.Count == 0) return;
        File.WriteAllText(
            Path.Combine(appDirectory, "config.js"),
            FrontendRuntimeConfig.BuildConfigJs(server.ServerName, settings, DateTime.UtcNow),
            new UTF8Encoding(false));
    }

    private async Task<License?> ResolveLicenseAsync(
        DownloadArtifactPackageRequest request, ApplicationServer server, CancellationToken cancellationToken)
    {
        if (request.LicensingMode != LicensingMode.WithLicense) return null;

        if (server.ServerType == ServerType.Frontend)
            throw new LicensingException("A frontend package cannot carry a licence; build the API package with the licence instead.");

        if (request.LicenseId == null)
            throw new LicensingException("Select the licence to include, or download the package without a licence.");

        var license = await _context.Licenses
            .Include(l => l.Client)
            .FirstOrDefaultAsync(l => l.Id == request.LicenseId.Value, cancellationToken)
            ?? throw new LicensingException($"Licence {request.LicenseId} was not found.");

        var serverClientId = await _context.Applications
            .Where(a => a.Id == server.ApplicationId)
            .Select(a => (int?)a.ClientId)
            .FirstOrDefaultAsync(cancellationToken);

        if (serverClientId == null)
            throw new LicensingException("This server's application has no client, so no licence can be matched to it.");

        if (license.ClientId != serverClientId.Value)
            throw new LicensingException(
                $"Licence {license.LicenseId} belongs to a different client than this server, so it cannot be packaged for it.");

        if (license.Status == LicenseStatus.Revoked)
            throw new LicensingException($"Licence {license.LicenseId} has been revoked.");

        if (license.ExpiresAt <= DateTime.UtcNow)
            throw new LicensingException(
                $"Licence {license.LicenseId} expired on {license.ExpiresAt:yyyy-MM-dd}. Renew it before building the package.");

        return license;
    }

    private static List<(string Name, string Path)> FindContentFolders(string contentDirectory, string? artifactSubfolder)
    {
        if (string.IsNullOrWhiteSpace(artifactSubfolder)) return new List<(string, string)>();

        var artifactRoot = Directory.GetParent(contentDirectory);
        if (artifactRoot == null) return new List<(string, string)>();

        return artifactRoot.GetDirectories()
            .Where(d => !d.FullName.Equals(contentDirectory, StringComparison.OrdinalIgnoreCase))
            .Where(d => d.EnumerateFiles("*", SearchOption.AllDirectories).Any())
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => (d.Name, d.FullName))
            .ToList();
    }

    private static void RemoveIntermediateFiles(string workspace, string zipPath)
    {
        foreach (var directory in Directory.GetDirectories(workspace))
            TryDeleteDirectory(directory);

        foreach (var file in Directory.GetFiles(workspace))
        {
            if (file.Equals(zipPath, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(file); } catch (IOException) { }
        }
    }

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
