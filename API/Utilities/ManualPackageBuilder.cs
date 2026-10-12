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
    public const string DatabaseKitFolder = "database";
    public const string BackupFolder = "backup";
    public const string DbMigratorFolder = "dbmigrator";

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
        IReadOnlyList<(string EntryName, string SourcePath)>? extraFiles = null,
        Action<long, long>? onProgress = null)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var hasScripts = !string.IsNullOrWhiteSpace(scriptsDirectory) && Directory.Exists(scriptsDirectory);
        var progress = new ZipProgress(onProgress,
            DirectorySize(appDirectory)
            + (hasScripts ? DirectorySize(scriptsDirectory!) : 0)
            + contentFolders.Sum(f => DirectorySize(f.Path))
            + (extraFiles?.Where(f => File.Exists(f.SourcePath)).Sum(f => new FileInfo(f.SourcePath).Length) ?? 0));

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        AddDirectory(archive, appDirectory, AppFolder, progress);

        if (hasScripts)
            AddDirectory(archive, scriptsDirectory!, ScriptsFolder, progress);

        foreach (var folder in contentFolders)
            AddDirectory(archive, folder.Path, $"{ContentFolder}/{folder.Name}", progress);

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
                progress.Add(new FileInfo(sourcePath).Length);
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

    private static void AddDirectory(ZipArchive archive, string sourceDirectory, string entryPrefix, ZipProgress? progress = null)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, $"{entryPrefix}/{relativePath}", CompressionLevel.Fastest);
            progress?.Add(new FileInfo(file).Length);
        }
    }

    private static long DirectorySize(string directory) =>
        Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : 0;

    /// <summary>Reports (bytes zipped, total bytes) at most twice a second, and once at the end.</summary>
    private sealed class ZipProgress(Action<long, long>? callback, long total)
    {
        private long _done;
        private DateTime _last = DateTime.MinValue;

        public void Add(long bytes)
        {
            if (callback == null) return;
            _done += bytes;
            if (_done >= total || DateTime.UtcNow - _last > TimeSpan.FromMilliseconds(500))
            {
                _last = DateTime.UtcNow;
                callback(_done, total);
            }
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
        text.AppendLine($"Database Mode:      {(config.ServerType == "Frontend" ? "n/a (frontend)" : config.SeparateDatabase ? "Handled by the database kit (run it first)" : config.FreshDatabase ? "Fresh Database" : "Existing Database")}");
        text.AppendLine($"Certificate:        {(config.CertificateMode == "UserProvided" ? "Packaged .pfx (password prompted on the server)" : "Default / already-installed certificate")}");
        text.AppendLine($"Installers:         {(config.IncludesInstallers ? "Included (installed when missing)" : "Not included - IIS, .NET Hosting Bundle" + (config.ServerType == "Frontend" ? ", URL Rewrite" : ", Memurai") + " must already be installed")}");
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
        string orchestrator,
        string? databaseKitDirectory = null,
        Action<long, long>? onProgress = null)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var hasDatabaseKit = !string.IsNullOrWhiteSpace(databaseKitDirectory);
        var progress = new ZipProgress(onProgress,
            DirectorySize(apiKitDirectory) + DirectorySize(frontendKitDirectory) + (hasDatabaseKit ? DirectorySize(databaseKitDirectory!) : 0));

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        if (hasDatabaseKit)
            AddDirectory(archive, databaseKitDirectory!, DatabaseKitFolder, progress);
        AddDirectory(archive, apiKitDirectory, ApiKitFolder, progress);
        AddDirectory(archive, frontendKitDirectory, FrontendKitFolder, progress);

        var scriptEntry = archive.CreateEntry(DeployScriptName, CompressionLevel.Optimal);
        using (var scriptWriter = new StreamWriter(scriptEntry.Open(), new UTF8Encoding(false)))
            scriptWriter.Write(orchestrator);

        var entry = archive.CreateEntry(ReadmeName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(readme);
    }

    public static string BuildCombinedDeployScript() => """
# DeployVare three-tier manual deployment, all tiers on THIS machine. Run as Administrator: .\Deploy.ps1
# Runs database\Deploy.ps1, then api\Deploy.ps1, then frontend\Deploy.ps1. Each folder is a complete kit:
# for separate database / API / frontend servers, copy one folder to each server and run its Deploy.ps1.
# The database settings are asked once here and handed to the database and API kits so they match.
# -UseDefaults deploys with the values the package was built with, without asking.
param([switch]$UseDefaults)
$ErrorActionPreference = 'Stop'
$PackageRoot = $PSScriptRoot

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host 'Administrator privileges are required. Please run Deploy.ps1 as Administrator.'
        exit 1
    }
}

function Read-Setting([string]$Label, [string]$Default) {
    if ($UseDefaults) { return $Default }
    $answer = Read-Host "$Label [$Default]"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim()
}

function Invoke-Kit([string]$Name, [string[]]$KitArgs) {
    $dir = Join-Path $PackageRoot $Name
    $script = Join-Path $dir 'Deploy.ps1'
    if (-not (Test-Path -LiteralPath $script)) {
        Write-Host "FAILED: $Name\Deploy.ps1 was not found."
        exit 1
    }
    Write-Host ''
    Write-Host "======== $Name ========"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script @KitArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: the $Name kit exited with code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

Assert-Admin

$shared = @()
if ($UseDefaults) { $shared += '-UseDefaults' }

$dbArgs = @()
$dbConfigPath = Join-Path $PackageRoot 'database\deploy-config.json'
$hasDatabaseKit = Test-Path -LiteralPath $dbConfigPath
if ($hasDatabaseKit) {
    $db = Get-Content -LiteralPath $dbConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $UseDefaults) { Write-Host 'Database (press Enter to keep the value in brackets):' }
    $instance = [string]$db.sqlInstanceName
    if (-not $instance) { $instance = 'SQLEXPRESS' }
    if ([bool]$db.freshDatabase) {
        $instance = Read-Setting 'SQL Server instance to install or use' $instance
        $source = if ($instance -eq 'MSSQLSERVER') { '.' } else { '.\' + $instance }
    } else {
        $source = Read-Setting 'SQL Server' ([string]$db.sqlDataSource)
    }
    $name = Read-Setting 'Database name' ([string]$db.databaseName)
    $dbArgs = @('-DatabaseName', $name, '-SqlDataSource', $source, '-SqlInstanceName', $instance)
    Invoke-Kit 'database' ($shared + $dbArgs)
}

Invoke-Kit 'api' ($shared + $dbArgs)
Invoke-Kit 'frontend' $shared
Write-Host ''
Write-Host 'Three-tier deployment completed successfully (database, API and frontend on this machine).'
exit 0
""";

    public static string BuildCombinedReadme(
        string applicationName,
        string apiServerName,
        string apiBuild,
        string frontendServerName,
        string frontendBuild,
        bool hasDatabaseKit = false,
        bool freshDatabase = false,
        string? databaseName = null)
    {
        var text = new StringBuilder();
        text.AppendLine("DEPLOYVARE THREE-TIER MANUAL DEPLOYMENT");
        text.AppendLine();
        text.AppendLine($"Application:        {applicationName}");
        if (hasDatabaseKit)
            text.AppendLine($"Database:           {databaseName ?? "(asked on the server)"}  ({(freshDatabase ? "Fresh Database: baseline restore" : "Existing Database")}, then DbMigrator)");
        text.AppendLine($"API server:         {apiServerName}  (build #{apiBuild})");
        text.AppendLine($"Frontend server:    {frontendServerName}  (build #{frontendBuild})");
        text.AppendLine();
        text.AppendLine("Folders (each one is a complete kit with its own Deploy.ps1):");
        if (hasDatabaseKit)
            text.AppendLine("  database\\   SQL Server (installed if needed), baseline restore, DbMigrator");
        text.AppendLine("  api\\        IIS site for the API, .NET Hosting Bundle, Memurai (Redis)");
        text.AppendLine("  frontend\\   IIS site for the frontend, URL Rewrite");
        text.AppendLine();
        text.AppendLine("All three tiers on ONE server:");
        text.AppendLine("  1. Copy the complete folder to the server.");
        text.AppendLine("  2. Open PowerShell as Administrator in that folder.");
        text.AppendLine("  3. Run:");
        text.AppendLine();
        text.AppendLine("       .\\Deploy.ps1");
        text.AppendLine();
        text.AppendLine("  It asks for the database settings once, then runs database, API and frontend in that order.");
        text.AppendLine();
        text.AppendLine("Separate servers (three-tier):");
        text.AppendLine("  Copy each folder to its own server and run that folder's Deploy.ps1 as Administrator,");
        text.AppendLine("  in this order: database\\ first, then api\\, then frontend\\.");
        text.AppendLine("  With SQL Server on another machine, the API kit cannot grant its IIS app pool account");
        text.AppendLine("  (a local account) access to the database: put a SQL login in the API's appsettings.json");
        text.AppendLine("  connection string, or grant the API server's domain machine account.");
        text.AppendLine();
        text.AppendLine("Every Deploy.ps1 shows the names it will use (IIS site, app pool, folder, host name,");
        text.AppendLine("SQL Server, database) and lets you keep each one with Enter or type a new value.");
        text.AppendLine("Add -UseDefaults to deploy with the packaged values without being asked.");
        text.AppendLine();
        return text.ToString();
    }

    public static string BuildDatabaseReadme(ManualDeployConfig config, string buildNumber, int sqlFileCount)
    {
        var text = new StringBuilder();
        text.AppendLine("DEPLOYVARE DATABASE KIT");
        text.AppendLine();
        text.AppendLine($"Build:              #{buildNumber}");
        text.AppendLine($"SQL Server:         {config.SqlDataSource}");
        text.AppendLine($"Database:           {config.DatabaseName}");
        text.AppendLine($"Database Mode:      {(config.FreshDatabase ? "Fresh Database (installs SQL Express if needed, restores the baseline backup)" : "Existing Database (must already exist)")}");
        text.AppendLine($"SQL scripts:        {sqlFileCount} (run by DbMigrator)");
        text.AppendLine($"Installers:         {(config.IncludesInstallers ? "Included (.NET runtime" + (config.FreshDatabase ? ", SQL Server Express" : "") + ")" : "Not included - the .NET 10 runtime" + (config.FreshDatabase ? " and SQL Server" : "") + " must already be installed")}");
        text.AppendLine();
        text.AppendLine("Instructions:");
        text.AppendLine("  1. Copy this folder to the database server (or the single server for an all-in-one install).");
        text.AppendLine("  2. Open PowerShell as Administrator in this folder.");
        text.AppendLine("  3. Run:");
        text.AppendLine();
        text.AppendLine("       .\\Deploy.ps1");
        text.AppendLine();
        text.AppendLine("  4. Then deploy the api\\ kit, then the frontend\\ kit.");
        text.AppendLine();
        text.AppendLine("Deploy.ps1 installs the .NET runtime DbMigrator needs, installs SQL Server Express when Fresh");
        text.AppendLine("Database is chosen and none is installed (it prompts for the sa password, never logged),");
        text.AppendLine("restores the baseline backup unless the database already exists (it is never dropped), and");
        text.AppendLine("runs DbMigrator with Windows authentication.");
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
