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
        text.AppendLine($"Certificate:        {(config.CertificateMode == "UserProvided" ? "Packaged .pfx (password prompted on the server)" : "A certificate already on the server that covers the host name (asks to import a .pfx when there is none)")}");
        text.AppendLine("HTTPS certificate:  Run .\\Deploy.ps1 -FixCertificate later to bind the real certificate to the deployed site.");
        text.AppendLine($"Installers:         {(config.IncludesInstallers ? "Included (installed when missing)" : "Not included - anything missing (" + (config.ServerType == "Frontend" ? "IIS URL Rewrite" : ".NET Hosting Bundle, .NET SDK, Memurai") + (config.FreshDatabase ? ", SQL Server Express" : "") + ") is installed from -InstallersPath or C:\\DeployVare\\Installers, otherwise downloaded from its official source")}");
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

    /// <summary>README section about the backend's app\appsettings.json.</summary>
    public static string BuildAppSettingsReadme(bool generated, string? basedOn)
    {
        var text = new StringBuilder();
        text.AppendLine("APPSETTINGS.JSON");
        text.AppendLine();
        if (generated)
        {
            text.AppendLine("The build has no appsettings.json, so DeployVare added a template at app\\appsettings.json");
            text.AppendLine(basedOn == null
                ? "(a minimal layout: add any other settings the application needs)."
                : $"(based on {basedOn} from the build).");
            text.AppendLine("Before running Deploy.ps1, open it in a text editor and set ConnectionStrings: replace every");
            text.AppendLine("<...> value with the real server, database and login.");
            text.AppendLine();
            text.AppendLine("When the server already has an appsettings.json, Deploy.ps1 keeps the server's file and this");
            text.AppendLine("template is not copied. Delete the server's file first to replace it.");
        }
        else
        {
            text.AppendLine("app\\appsettings.json is the file from the build. Check its ConnectionStrings before running");
            text.AppendLine("Deploy.ps1; it replaces the server's file unless appsettings.json is a kept-on-server path.");
        }
        text.AppendLine();
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

    /// <summary>One zip of a two- or three-tier download, e.g. AuditVare_database-1.0.0277_manual-deploy.zip.</summary>
    public static string BuildTierFileName(string applicationName, string tier, string buildNumber) =>
        $"{Sanitize(applicationName)}_{Sanitize(tier)}-{Sanitize(buildNumber)}_manual-deploy.zip";

    /// <summary>Zips a finished kit folder (its files at the zip root).</summary>
    public static void ZipDirectory(string sourceDirectory, string zipPath, Action<long, long>? onProgress = null)
    {
        var directory = Path.GetDirectoryName(zipPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var progress = new ZipProgress(onProgress, DirectorySize(sourceDirectory));
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Fastest);
            progress.Add(new FileInfo(file).Length);
        }
    }

    public const string AllTiersScriptName = "Deploy-AllTiers.ps1";

    /// <summary>
    /// Added to every zip of a two- or three-tier download. Each zip is a complete kit for its own server; this script
    /// is only for putting every tier on ONE machine. It finds the other zips (or their extracted folders) of the same
    /// download next to its own folder, matched by packageSetId, extracts them, and runs database, API, frontend.
    /// </summary>
    public static string BuildAllTiersScript() => """
# DeployVare: every tier of this download on THIS machine. Run as Administrator: .\Deploy-AllTiers.ps1
# Each zip of the download is a complete kit for its own server (run its Deploy.ps1 there). For one machine, put all
# the zips of the download in one folder, extract any one of them, and run this script from it: it extracts the
# others and runs database (three-tier), then API, then frontend. The database settings are asked once.
# -UseDefaults deploys with the packaged values without asking. -InstallersPath <folder> is handed to every kit.
param([switch]$UseDefaults, [string]$InstallersPath)
$ErrorActionPreference = 'Stop'
$Here = $PSScriptRoot
$Parent = Split-Path -Parent $Here

function Assert-Admin {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Write-Host 'Administrator privileges are required. Please run Deploy-AllTiers.ps1 as Administrator.'
        exit 1
    }
}

function Read-Setting([string]$Label, [string]$Default) {
    if ($UseDefaults) { return $Default }
    $answer = Read-Host "$Label [$Default]"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $Default }
    return $answer.Trim()
}

function Read-KitConfig([string]$Dir) {
    $path = Join-Path $Dir 'deploy-config.json'
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    try { return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json } catch { return $null }
}

Assert-Admin
$own = Read-KitConfig $Here
if (-not $own -or -not $own.packageSetId) {
    Write-Host 'FAILED: deploy-config.json next to this script has no packageSetId (not a two- or three-tier download).'
    exit 1
}
$setId = [string]$own.packageSetId
$kits = @{}
function Add-Kit([string]$Dir) {
    $cfg = Read-KitConfig $Dir
    if ($cfg -and [string]$cfg.packageSetId -eq $setId -and -not $kits.ContainsKey([string]$cfg.serverType)) {
        $kits[[string]$cfg.serverType] = @{ Dir = $Dir; Cfg = $cfg }
    }
}

Add-Kit $Here
Get-ChildItem -LiteralPath $Parent -Directory -ErrorAction SilentlyContinue | ForEach-Object { Add-Kit $_.FullName }

# Zips of the same download that are not extracted yet
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($zipFile in @(Get-ChildItem -LiteralPath $Parent -Filter '*.zip' -File -ErrorAction SilentlyContinue)) {
    $cfg = $null
    try {
        $zip = [IO.Compression.ZipFile]::OpenRead($zipFile.FullName)
        try {
            $entry = $zip.GetEntry('deploy-config.json')
            if ($entry) {
                $reader = New-Object IO.StreamReader($entry.Open())
                try { $cfg = $reader.ReadToEnd() | ConvertFrom-Json } finally { $reader.Dispose() }
            }
        } finally { $zip.Dispose() }
    } catch { continue }
    if (-not $cfg -or [string]$cfg.packageSetId -ne $setId -or $kits.ContainsKey([string]$cfg.serverType)) { continue }
    $dest = Join-Path $Parent ([IO.Path]::GetFileNameWithoutExtension($zipFile.Name))
    Write-Host "Extracting $($zipFile.Name)..."
    try { Unblock-File -LiteralPath $zipFile.FullName -ErrorAction SilentlyContinue } catch { }
    Expand-Archive -LiteralPath $zipFile.FullName -DestinationPath $dest -Force
    Add-Kit $dest
}

$needDatabase = $kits.ContainsKey('Api') -and [bool]$kits['Api'].Cfg.separateDatabase
$missing = @()
if ($needDatabase -and -not $kits.ContainsKey('Database')) { $missing += 'database' }
if (-not $kits.ContainsKey('Api')) { $missing += 'api' }
if (-not $kits.ContainsKey('Frontend')) { $missing += 'frontend' }
if ($missing.Count -gt 0) {
    Write-Host "FAILED: the $($missing -join ', ') zip(s) of this download were not found in $Parent."
    Write-Host 'Put every zip of the download in that folder (extracted or not) and run this script again.'
    exit 1
}

function Invoke-Kit([string]$Tier, [string[]]$KitArgs) {
    $script = Join-Path $kits[$Tier].Dir 'Deploy.ps1'
    Write-Host ''
    Write-Host "======== $Tier ========"
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $script @KitArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: the $Tier kit exited with code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

$shared = @()
if ($UseDefaults) { $shared += '-UseDefaults' }
if ($InstallersPath) { $shared += @('-InstallersPath', $InstallersPath) }

$dbArgs = @()
if ($needDatabase) {
    $db = $kits['Database'].Cfg
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
    Invoke-Kit 'Database' ($shared + $dbArgs)
}

Invoke-Kit 'Api' ($shared + $dbArgs)
Invoke-Kit 'Frontend' $shared
Write-Host ''
Write-Host "All tiers deployed successfully on this machine ($(if ($needDatabase) { 'database, ' })API and frontend)."
exit 0
""";

    /// <summary>README section added to each zip of a two- or three-tier download.</summary>
    public static string BuildTierSetReadme(bool threeTier)
    {
        var text = new StringBuilder();
        text.AppendLine(threeTier ? "PART OF A THREE-TIER DOWNLOAD" : "PART OF A TWO-TIER DOWNLOAD");
        text.AppendLine();
        text.AppendLine(threeTier
            ? "This download is one zip per server: *_database-*, *_api-* and *_frontend-*.zip."
            : "This download is one zip per server: *_api-* (API and database) and *_frontend-*.zip.");
        text.AppendLine();
        text.AppendLine("Separate servers: copy each zip to its own server, extract it and run its Deploy.ps1 as");
        text.AppendLine(threeTier
            ? "Administrator, in this order: database first, then API, then frontend."
            : "Administrator: the API zip first (it also sets up the database), then the frontend zip.");
        if (threeTier)
        {
            text.AppendLine("With SQL Server on another machine, the API kit cannot grant its IIS app pool account");
            text.AppendLine("(a local account) access to the database: put a SQL login in the API's appsettings.json");
            text.AppendLine("connection string, or grant the API server's domain machine account.");
        }
        text.AppendLine();
        text.AppendLine("Everything on ONE machine: put all the zips in one folder, extract any one of them, and run");
        text.AppendLine($"  .\\{AllTiersScriptName}");
        text.AppendLine("from it as Administrator. It extracts the other zips and deploys every tier in order,");
        text.AppendLine("asking for the database settings once. Add -UseDefaults to keep the packaged values.");
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
        text.AppendLine($"Installers:         {(config.IncludesInstallers ? "Included (.NET runtime" + (config.FreshDatabase ? ", SQL Server Express" : "") + ")" : "Not included - anything missing (.NET 10 runtime" + (config.FreshDatabase ? ", SQL Server Express" : "") + ") is installed from -InstallersPath or C:\\DeployVare\\Installers, otherwise downloaded from its official source")}");
        text.AppendLine();
        text.AppendLine("Instructions:");
        text.AppendLine("  1. Copy this folder to the database server (or the single server for an all-in-one install).");
        text.AppendLine("  2. Open PowerShell as Administrator in this folder.");
        text.AppendLine("  3. Run:");
        text.AppendLine();
        text.AppendLine("       .\\Deploy.ps1");
        text.AppendLine();
        text.AppendLine("  4. Then deploy the API zip, then the frontend zip.");
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
