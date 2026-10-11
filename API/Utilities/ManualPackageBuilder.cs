using System.IO.Compression;
using System.Text;
using API.Models;

namespace API.Utilities;

/// <summary>
/// Builds the ZIP offered for a manual deployment: application files, SQL scripts, content folders,
/// offline installers, Deploy.ps1 and (optionally) a licence.
/// </summary>
public static class ManualPackageBuilder
{
    public const string AppFolder = "app";
    public const string ScriptsFolder = "sqlscripts";
    public const string ContentFolder = "content";
    public const string LicenseFolder = "licence";
    public const string InstallersFolder = "installers";
    public const string DatabaseFolder = "database";
    public const string CertificateFolder = "certificate";
    public const string ReadmeName = "README.txt";
    public const string DeployScriptName = "Deploy.ps1";
    public const string ConfigName = "deploy-config.json";
    public const string ApiKitFolder = "api";
    public const string FrontendKitFolder = "frontend";

    public static void Build(
        string zipPath,
        string appDirectory,
        string? scriptsDirectory,
        IReadOnlyList<(string Name, string Path)> contentFolders,
        string readme,
        string? licenseFileName = null,
        string? licenseContent = null,
        string? deployScript = null,
        string? configJson = null,
        IReadOnlyList<(string EntryName, string SourcePath)>? extraFiles = null)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        AddDirectory(archive, appDirectory, AppFolder);

        if (!string.IsNullOrWhiteSpace(scriptsDirectory) && Directory.Exists(scriptsDirectory))
            AddDirectory(archive, scriptsDirectory!, ScriptsFolder);

        foreach (var folder in contentFolders)
            AddDirectory(archive, folder.Path, $"{ContentFolder}/{folder.Name}");

        if (!string.IsNullOrWhiteSpace(licenseFileName) && !string.IsNullOrWhiteSpace(licenseContent))
        {
            var licenceEntry = archive.CreateEntry($"{LicenseFolder}/{licenseFileName}", CompressionLevel.Optimal);
            using var licenceWriter = new StreamWriter(licenceEntry.Open(), new UTF8Encoding(false));
            licenceWriter.Write(licenseContent);
        }

        if (!string.IsNullOrWhiteSpace(deployScript))
        {
            var scriptEntry = archive.CreateEntry(DeployScriptName, CompressionLevel.Optimal);
            using var scriptWriter = new StreamWriter(scriptEntry.Open(), new UTF8Encoding(false));
            scriptWriter.Write(deployScript);
        }

        if (!string.IsNullOrWhiteSpace(configJson))
        {
            var configEntry = archive.CreateEntry(ConfigName, CompressionLevel.Optimal);
            using var configWriter = new StreamWriter(configEntry.Open(), new UTF8Encoding(false));
            configWriter.Write(configJson);
        }

        if (extraFiles != null)
        {
            foreach (var (entryName, sourcePath) in extraFiles)
            {
                if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) continue;
                archive.CreateEntryFromFile(sourcePath, entryName.Replace('\\', '/'), CompressionLevel.Fastest);
            }
        }

        var entry = archive.CreateEntry(ReadmeName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(readme);
    }

    public static string LoadDeployScript()
    {
        foreach (var candidate in DeployScriptCandidates())
        {
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new InvalidOperationException("Deploy.ps1 was not found. Rebuild the API so DeploymentScripts/Deploy.ps1 is copied to the output folder.");
    }

    public static IEnumerable<string> DeployScriptCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "DeploymentScripts", DeployScriptName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), "DeploymentScripts", DeployScriptName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), "API", "DeploymentScripts", DeployScriptName);
    }

    private static void AddDirectory(ZipArchive archive, string sourceDirectory, string entryPrefix)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, $"{entryPrefix}/{relativePath}", CompressionLevel.Fastest);
        }
    }

    public static string BuildReadme(ManualDeployConfig config, string buildNumber, IReadOnlyList<string> keepOnServerPaths)
    {
        var text = new StringBuilder();
        text.AppendLine("DEPLOYVARE MANUAL DEPLOYMENT");
        text.AppendLine();
        text.AppendLine($"Build:              #{buildNumber}");
        text.AppendLine($"Target Server:      {config.ServerName}");
        text.AppendLine($"IIS Site:           {config.WebsiteName}");
        text.AppendLine($"IIS App Pool:       {config.AppPoolName}");
        text.AppendLine($"IIS Physical Path:  {config.DeploymentPath}");
        text.AppendLine($"Site URL:           https://{config.SiteHostName}");
        text.AppendLine($"Database Mode:      {(config.ServerType == "Frontend" ? "n/a (frontend)" : config.FreshDatabase ? "Fresh Database" : "Existing Database")}");
        text.AppendLine($"Certificate:        {(config.CertificateMode == "UserProvided" ? "Packaged .pfx (password prompted on the server)" : "Default / already-installed certificate")}");
        if (config.ServerType != "Frontend")
            text.AppendLine($"Redis:              Memurai (Redis {config.RequiredRedisMajor} on port {config.RedisPort})");
        if (!string.IsNullOrWhiteSpace(config.LicenseFileName))
            text.AppendLine($"Licence:            {config.LicenseFileName} (activate before starting the site)");
        text.AppendLine();
        text.AppendLine("Instructions:");
        text.AppendLine("  1. Copy the complete deployment package to the target Windows Server.");
        text.AppendLine("  2. Open PowerShell as Administrator.");
        text.AppendLine("  3. Navigate to the package directory.");
        text.AppendLine("  4. Run:");
        text.AppendLine();
        text.AppendLine("       .\\Deploy.ps1");
        text.AppendLine();
        text.AppendLine("  5. Wait for the deployment to complete.");
        text.AppendLine();
        text.AppendLine("Do not manually:");
        text.AppendLine("  - Install IIS");
        text.AppendLine("  - Install .NET");
        text.AppendLine("  - Install Redis / Memurai");
        text.AppendLine("  - Create the IIS site or app pool");
        text.AppendLine("  - Restore the database");
        text.AppendLine("  - Copy application files");
        text.AppendLine("  - Configure the certificate or IIS binding");
        text.AppendLine("  - Run DbMigrator");
        text.AppendLine();
        text.AppendLine("Deploy.ps1 handles these steps.");
        text.AppendLine();
        text.AppendLine("The script never prints SQL or certificate passwords. If SQL Express or a .pfx must be");
        text.AppendLine("installed, it prompts on the server.");
        text.AppendLine();
        if (keepOnServerPaths.Count > 0 || config.PreserveWebConfig)
        {
            text.AppendLine("Files kept on the server when they already exist:");
            if (config.PreserveWebConfig) text.AppendLine("  web.config");
            foreach (var path in keepOnServerPaths) text.AppendLine($"  {path}");
            text.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(config.LicenseFileName))
        {
            text.AppendLine("Licensing:");
            text.AppendLine("  Deploy.ps1 installs the .lic file and writes C:\\Temp\\activation-request.json.");
            text.AppendLine("  Email that file to the vendor, then on the server run:");
            text.AppendLine("    <application>.exe --license-apply C:\\Temp\\activation-response.json");
            text.AppendLine("    <application>.exe --license-status");
            text.AppendLine("  Start the app pool and site only after --license-status succeeds.");
            text.AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Instructions for whoever copies the files to the server by hand.</summary>
    public static string BuildReadme(
        string serverName,
        string deploymentPath,
        string buildNumber,
        bool isFrontend,
        bool hasScripts,
        IReadOnlyList<string> contentFolders,
        IReadOnlyList<string> keepOnServerPaths,
        bool preserveWebConfig,
        string? licenseFileName = null,
        string? licenseId = null)
    {
        return BuildReadme(new ManualDeployConfig
        {
            ServerName = serverName,
            ServerType = isFrontend ? "Frontend" : "Api",
            WebsiteName = serverName,
            AppPoolName = serverName,
            DeploymentPath = deploymentPath,
            SiteHostName = "",
            PreserveWebConfig = preserveWebConfig,
            KeepOnServerPaths = keepOnServerPaths.ToList(),
            HasScripts = hasScripts,
            ContentFolders = contentFolders.ToList(),
            LicenseFileName = licenseFileName,
            LicensingMode = string.IsNullOrWhiteSpace(licenseFileName) ? "WithoutLicense" : "WithLicense"
        }, buildNumber, keepOnServerPaths);
    }

    /// <summary>File name for the download, without characters Windows or browsers dislike.</summary>
    public static string BuildFileName(string serverName, string buildNumber)
    {
        var safeServer = Sanitize(serverName);
        var safeBuild = Sanitize(buildNumber);
        return $"{safeServer}_{safeBuild}_manual-deploy.zip";
    }

    public static string BuildCombinedFileName(string applicationName, string apiBuild, string frontendBuild)
    {
        return $"{Sanitize(applicationName)}_api-{Sanitize(apiBuild)}_frontend-{Sanitize(frontendBuild)}_manual-deploy.zip";
    }

    public static void BuildCombined(
        string zipPath,
        string apiKitDirectory,
        string frontendKitDirectory,
        string readme,
        string orchestrator)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        AddDirectory(archive, apiKitDirectory, ApiKitFolder);
        AddDirectory(archive, frontendKitDirectory, FrontendKitFolder);

        var scriptEntry = archive.CreateEntry(DeployScriptName, CompressionLevel.Optimal);
        using (var scriptWriter = new StreamWriter(scriptEntry.Open(), new UTF8Encoding(false)))
            scriptWriter.Write(orchestrator);

        var entry = archive.CreateEntry(ReadmeName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(readme);
    }

    public static string BuildCombinedDeployScript() => """
# DeployVare combined manual deployment. Run as Administrator from this folder: .\Deploy.ps1
# Deploys the API kit first, then the frontend kit. Each kit is the same as This server only.
$ErrorActionPreference = 'Stop'
$PackageRoot = $PSScriptRoot

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host 'Administrator privileges are required. Please run Deploy.ps1 as Administrator.'
        exit 1
    }
}

function Invoke-Kit([string]$Name) {
    $dir = Join-Path $PackageRoot $Name
    $script = Join-Path $dir 'Deploy.ps1'
    if (-not (Test-Path -LiteralPath $script)) {
        Write-Host "FAILED: $Name\Deploy.ps1 was not found."
        exit 1
    }
    Write-Host ''
    Write-Host "======== $Name ========"
    $p = Start-Process -FilePath 'powershell.exe' -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $script) -WorkingDirectory $dir -Wait -PassThru -NoNewWindow
    if ($p.ExitCode -ne 0) {
        Write-Host "FAILED: the $Name kit exited with code $($p.ExitCode)."
        exit $p.ExitCode
    }
}

Assert-Admin
Invoke-Kit 'api'
Invoke-Kit 'frontend'
Write-Host ''
Write-Host 'Combined deployment completed successfully.'
exit 0
""";

    public static string BuildCombinedReadme(
        string applicationName,
        string apiServerName,
        string apiBuild,
        string frontendServerName,
        string frontendBuild)
    {
        var text = new StringBuilder();
        text.AppendLine("DEPLOYVARE COMBINED MANUAL DEPLOYMENT");
        text.AppendLine();
        text.AppendLine($"Application:        {applicationName}");
        text.AppendLine($"API server:         {apiServerName}  (build #{apiBuild})");
        text.AppendLine($"Frontend server:    {frontendServerName}  (build #{frontendBuild})");
        text.AppendLine();
        text.AppendLine("This zip is the API package and the frontend package together.");
        text.AppendLine("Each folder (api\\ and frontend\\) is the same kit you would get from This server only.");
        text.AppendLine();
        text.AppendLine("Instructions:");
        text.AppendLine("  1. Copy the complete folder to the target Windows Server.");
        text.AppendLine("  2. Open PowerShell as Administrator.");
        text.AppendLine("  3. Run:");
        text.AppendLine();
        text.AppendLine("       .\\Deploy.ps1");
        text.AppendLine();
        text.AppendLine("  That deploys the API first, then the frontend.");
        text.AppendLine("  To deploy only one side, run api\\Deploy.ps1 or frontend\\Deploy.ps1 instead.");
        text.AppendLine();
        return text.ToString();
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string((value ?? string.Empty)
            .Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-')
            .ToArray())
            .Trim('-');

        return string.IsNullOrEmpty(cleaned) ? "package" : cleaned;
    }
}
