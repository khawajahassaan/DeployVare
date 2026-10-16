using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using API.Models;

namespace API.Utilities;

/// <summary>
/// Assembles Linux deployment kits: a folder with app/ (and sqlscripts/ or dbmigrator/), Deploy.sh,
/// deploy-config.json and a README, packed as .tar.gz with Unix permissions set explicitly (so a kit built on
/// Windows still extracts with an executable Deploy.sh). The same kit is used for manual and one-click deployment.
/// </summary>
public static class LinuxKitBuilder
{
    public const string AppFolder = "app";
    public const string ScriptsFolder = "sqlscripts";
    public const string DbMigratorFolder = "dbmigrator";
    public const string DeployScriptName = "Deploy.sh";
    public const string ConfigName = "deploy-config.json";
    public const string ReadmeName = "README.md";

    private static readonly JsonSerializerOptions ConfigJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private const UnixFileMode Directory755 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private const UnixFileMode File644 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

    public static string LoadDeployScript()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "DeploymentScripts", DeployScriptName),
            Path.Combine(Directory.GetCurrentDirectory(), "DeploymentScripts", DeployScriptName),
            Path.Combine(Directory.GetCurrentDirectory(), "API", "DeploymentScripts", DeployScriptName)
        };
        var path = candidates.FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Deploy.sh was not found. Rebuild the API so DeploymentScripts/Deploy.sh is copied to the output folder.");
        // A Windows checkout may have turned the line endings into CRLF, which bash cannot run
        return File.ReadAllText(path).Replace("\r\n", "\n");
    }

    /// <summary>Writes Deploy.sh, deploy-config.json and README.md into a kit folder whose app/ etc. are already in place.</summary>
    public static void WriteKitFiles(string kitDirectory, LinuxDeployConfig config, string readme)
    {
        Directory.CreateDirectory(kitDirectory);
        var utf8 = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(kitDirectory, DeployScriptName), LoadDeployScript(), utf8);
        File.WriteAllText(Path.Combine(kitDirectory, ConfigName), JsonSerializer.Serialize(config, ConfigJson) + "\n", utf8);
        File.WriteAllText(Path.Combine(kitDirectory, ReadmeName), readme.Replace("\r\n", "\n"), utf8);
    }

    /// <summary>Copies a folder's contents into another (created when missing).</summary>
    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), overwrite: true);
    }

    /// <summary>
    /// Copies DbMigrator (DbMigrator.dll, every assembly next to it and the native runtimes) out of a published API
    /// folder for a database kit. Returns false when the build has no DbMigrator.dll.
    /// </summary>
    public static bool CopyDbMigrator(string appDirectory, string destination)
    {
        var dll = Path.Combine(appDirectory, "DbMigrator.dll");
        if (!File.Exists(dll)) return false;

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(appDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("DbMigrator.", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(destination, name), overwrite: true);
        }
        var runtimes = Path.Combine(appDirectory, "runtimes");
        if (Directory.Exists(runtimes))
            CopyDirectory(runtimes, Path.Combine(destination, "runtimes"));
        EnsureDbMigratorRuntimeConfig(destination, appDirectory);
        return true;
    }

    /// <summary>
    /// "dotnet DbMigrator.dll" needs DbMigrator.runtimeconfig.json, which an API publish usually leaves out (DbMigrator is
    /// only a referenced project there). Writes one targeting the API's .NET version when it is missing.
    /// </summary>
    public static void EnsureDbMigratorRuntimeConfig(string migratorDirectory, string appDirectory)
    {
        var target = Path.Combine(migratorDirectory, "DbMigrator.runtimeconfig.json");
        if (File.Exists(target) || !File.Exists(Path.Combine(migratorDirectory, "DbMigrator.dll"))) return;

        var major = 10;
        try
        {
            major = LinuxBuildInspector.FindBackendEntry(appDirectory).DotNetMajor;
        }
        catch (Exception)
        {
            // Keep the default: the entry point is checked (and reported) separately
        }

        var json = $$"""
            {
              "runtimeOptions": {
                "tfm": "net{{major}}.0",
                "rollForward": "Major",
                "framework": { "name": "Microsoft.NETCore.App", "version": "{{major}}.0.0" }
              }
            }
            """;
        File.WriteAllText(target, json + "\n", new UTF8Encoding(false));
    }

    /// <summary>
    /// Packs <paramref name="kitDirectory"/> as a .tar.gz whose entries sit under one folder named after the file,
    /// so "tar -xzf kit.tar.gz" creates that folder. Deploy.sh and the paths in <paramref name="executables"/>
    /// (relative, '/'-separated) are 0755; everything else 0644 (folders 0755).
    /// </summary>
    public static void CreateTarGz(string kitDirectory, string tarGzPath, IReadOnlyCollection<string> executables,
        Action<long, long>? onProgress = null)
    {
        var rootName = RootFolderName(tarGzPath);
        var executableSet = new HashSet<string>(executables.Append(DeployScriptName), StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(kitDirectory, "*", SearchOption.AllDirectories).ToList();
        var total = files.Sum(f => new FileInfo(f).Length);
        long done = 0;
        var lastReport = DateTime.MinValue;

        Directory.CreateDirectory(Path.GetDirectoryName(tarGzPath)!);
        using var output = File.Create(tarGzPath);
        using var gzip = new GZipStream(output, CompressionLevel.Fastest);
        using var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: false);

        var now = DateTimeOffset.UtcNow;
        tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, rootName + "/") { Mode = Directory755, ModificationTime = now });
        foreach (var directory in Directory.EnumerateDirectories(kitDirectory, "*", SearchOption.AllDirectories).OrderBy(d => d, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(kitDirectory, directory).Replace('\\', '/');
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, $"{rootName}/{relative}/") { Mode = Directory755, ModificationTime = now });
        }

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(kitDirectory, file).Replace('\\', '/');
            using var stream = File.OpenRead(file);
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, $"{rootName}/{relative}")
            {
                Mode = executableSet.Contains(relative) ? Directory755 : File644,
                ModificationTime = File.GetLastWriteTimeUtc(file),
                DataStream = stream
            });

            done += stream.Length;
            if (onProgress != null && (done >= total || DateTime.UtcNow - lastReport > TimeSpan.FromMilliseconds(500)))
            {
                lastReport = DateTime.UtcNow;
                onProgress(done, total);
            }
        }
    }

    /// <summary>e.g. AuditVare_linux-api-1.0.0277.tar.gz</summary>
    public static string BuildFileName(string applicationName, string tier, string buildNumber) =>
        $"{Sanitize(applicationName)}_linux-{Sanitize(tier)}-{Sanitize(buildNumber)}.tar.gz";

    public static string RootFolderName(string tarGzPath)
    {
        var name = Path.GetFileName(tarGzPath);
        return name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? name[..^".tar.gz".Length] : Path.GetFileNameWithoutExtension(name);
    }

    /// <summary>Lower-case name usable for a site file, systemd service and folder: "AuditVare Web" → "auditvare-web".</summary>
    public static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c)) builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? "app" : slug.Length > 60 ? slug[..60].Trim('-') : slug;
    }

    private static string Sanitize(string value)
    {
        var cleaned = new string((value ?? string.Empty)
            .Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '-')
            .ToArray()).Trim('-');
        return string.IsNullOrEmpty(cleaned) ? "package" : cleaned;
    }

    /// <summary>README.md of a kit: what is in it and the exact commands for this server.</summary>
    public static string BuildReadme(LinuxDeployConfig config, string fileName, int tierCount, string? note)
    {
        var folder = RootFolderName(fileName);
        var scheme = string.IsNullOrWhiteSpace(config.CertificatePath) ? "http" : "https";
        var text = new StringBuilder();
        text.AppendLine($"# {config.ApplicationName} — Linux {KitTitle(config.KitType)} kit (build #{config.BuildNumber})");
        text.AppendLine();
        if (!string.IsNullOrWhiteSpace(note))
        {
            text.AppendLine($"> {note}");
            text.AppendLine();
        }
        if (tierCount > 1)
        {
            text.AppendLine($"This kit is one of {tierCount} in this download. Deploy them in this order: "
                + (tierCount == 3 ? "**database → API → frontend**." : "**API → frontend**."));
            text.AppendLine();
        }

        text.AppendLine("## Deploy");
        text.AppendLine();
        text.AppendLine("Copy the kit to the server, extract it and run the script as root:");
        text.AppendLine();
        text.AppendLine("```bash");
        text.AppendLine($"scp {fileName} user@SERVER:/tmp/          # from your PC (or use WinSCP)");
        text.AppendLine("ssh user@SERVER");
        text.AppendLine($"cd /tmp && tar -xzf {fileName} && cd {folder}");
        if (config.KitType is "Api" or "Database")
        {
            text.AppendLine("sudo dnf install -y nano     # Red Hat / Rocky / AlmaLinux only, when nano is missing (or use vi)");
            text.AppendLine($"nano {(config.KitType == "Api" ? "app/" : "")}appsettings.json   # set ConnectionStrings first (see below)");
        }
        text.AppendLine("sudo bash Deploy.sh");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("Works on Ubuntu/Debian and on the Red Hat family (RHEL 8+, Rocky Linux, AlmaLinux, CentOS Stream). On Red Hat the script");
        text.AppendLine("also opens HTTP/HTTPS in firewalld and sets the SELinux permission and file labels the web server needs.");
        text.AppendLine();
        text.AppendLine("The script shows what it will do and asks before changing anything. `sudo bash Deploy.sh --help` lists its options.");
        text.AppendLine();

        switch (config.KitType)
        {
            case "Api":
                text.AppendLine("## What Deploy.sh does");
                text.AppendLine();
                text.AppendLine(config.SelfContained
                    ? "1. Skips .NET (this is a self-contained linux-x64 build)."
                    : $"1. Installs the ASP.NET Core {config.RequiredDotNetMajor} runtime when it is missing.");
                if (config.InstallRedis) text.AppendLine($"1. Installs Redis on port {config.RedisPort} when it is missing.");
                if (!string.IsNullOrWhiteSpace(config.SiteHostName)) text.AppendLine($"1. Installs {WebServerName(config.WebServer)} when it is missing.");
                text.AppendLine($"1. Backs up `{config.DeploymentPath}` to `{config.BackupPath}` (last 5 kept).");
                text.AppendLine($"1. Stops `{config.ServiceName}`, copies `app/` to `{config.DeploymentPath}`, keeping the server's own " +
                    (config.KeepOnServerPaths.Count > 0 ? string.Join(", ", config.KeepOnServerPaths.Select(p => $"`{p}`")) : "files you list") + ".");
                text.AppendLine($"1. `chown -R {config.ServiceUser}` and `chmod` (folders 750, files 640).");
                if (config.RunMigrations) text.AppendLine("1. Runs DbMigrator with the build's SQL scripts.");
                text.AppendLine($"1. Writes `/etc/systemd/system/{config.ServiceName}.service` (starts at boot, restarts on crash) and starts it on `http://127.0.0.1:{config.AppPort}`.");
                text.AppendLine(string.IsNullOrWhiteSpace(config.SiteHostName)
                    ? "1. Creates no web server site: the API is reached through the frontend's API route."
                    : $"1. Writes the {WebServerName(config.WebServer)} site `{config.SiteName}` that forwards `{scheme}://{config.SiteHostName}` to the app, tests it and reloads.");
                text.AppendLine();
                AppendAppSettings(text, config, "app/appsettings.json");
                text.AppendLine("## Everyday commands");
                text.AppendLine();
                text.AppendLine("```bash");
                text.AppendLine($"sudo systemctl status {config.ServiceName}      # is it running?");
                text.AppendLine($"sudo journalctl -u {config.ServiceName} -f      # live logs (Ctrl+C to stop)");
                text.AppendLine($"sudo systemctl restart {config.ServiceName}     # after editing appsettings.json");
                text.AppendLine($"sudo nano {config.DeploymentPath}/appsettings.json");
                text.AppendLine("```");
                break;

            case "Frontend":
                text.AppendLine("## What Deploy.sh does");
                text.AppendLine();
                text.AppendLine($"1. Installs {WebServerName(config.WebServer)} when it is missing.");
                text.AppendLine($"1. Backs up `{config.DeploymentPath}`, copies `app/` there, gives the files to the web server's user (`www-data`, or `nginx`/`apache` on Red Hat) and `chmod` (folders 755, files 644).");
                text.AppendLine($"1. Writes the site `{config.SiteName}` for `{scheme}://{config.SiteHostName}`: serves the files and sends unknown paths to `index.html` (single-page app).");
                if (!string.IsNullOrWhiteSpace(config.ApiProxyUpstream))
                    text.AppendLine($"1. Forwards `{config.ApiProxyPath ?? "/api"}/` to the backend at `{config.ApiProxyUpstream}`.");
                text.AppendLine();
                text.AppendLine("## Runtime settings");
                text.AppendLine();
                text.AppendLine($"`app/config.js` (on the server: `{config.DeploymentPath}/config.js`) holds the URLs the browser uses, such as");
                text.AppendLine("`VITE_API_URL`. Edit it with `nano` if the API address changes; no rebuild or restart is needed.");
                break;

            case "Database":
                text.AppendLine("## What Deploy.sh does");
                text.AppendLine();
                text.AppendLine("1. Installs the .NET runtime that DbMigrator needs when it is missing.");
                text.AppendLine("1. Reads the first entry of `ConnectionStrings` in `appsettings.json` next to the script.");
                text.AppendLine("1. Runs `dbmigrator/DbMigrator.dll` with the scripts in `sqlscripts/`.");
                text.AppendLine();
                text.AppendLine("Run it on any Linux machine that can reach SQL Server on port 1433 (the database server itself or the API server).");
                text.AppendLine("The database must already exist and be an application database that DbMigrator manages; create or restore it first.");
                text.AppendLine();
                AppendAppSettings(text, config, "appsettings.json");
                break;
        }

        text.AppendLine();
        text.AppendLine("## Roll back");
        text.AppendLine();
        text.AppendLine("Each run saves the previous version first. To restore it:");
        text.AppendLine();
        text.AppendLine("```bash");
        text.AppendLine($"ls -lt {config.BackupPath}/");
        if (config.KitType == "Api") text.AppendLine($"sudo systemctl stop {config.ServiceName}");
        text.AppendLine($"sudo tar -xzf {config.BackupPath}/{config.SiteName}_YYYYMMDD_HHMMSS.tar.gz -C {Path.GetDirectoryName(config.DeploymentPath)?.Replace('\\', '/')}");
        if (config.KitType == "Api") text.AppendLine($"sudo systemctl start {config.ServiceName}");
        text.AppendLine("```");
        return text.ToString();
    }

    private static void AppendAppSettings(StringBuilder text, LinuxDeployConfig config, string path)
    {
        text.AppendLine("## appsettings.json (database connection)");
        text.AppendLine();
        text.AppendLine(config.AppSettingsGenerated
            ? $"The build has no appsettings.json, so DeployVare added a template at `{path}`."
            : $"`{path}` is the file from the build.");
        text.AppendLine("Open it and replace every `<...>` value in `ConnectionStrings`:");
        text.AppendLine();
        text.AppendLine("```bash");
        text.AppendLine($"nano {path}       # Ctrl+O, Enter to save; Ctrl+X to exit");
        text.AppendLine("```");
        text.AppendLine();
        text.AppendLine("Linux cannot use Windows authentication, so use a SQL login:");
        text.AppendLine("`Server=10.0.0.5,1433;Database=AppDb;User Id=app_user;Password=...;TrustServerCertificate=True`");
        text.AppendLine();
        if (config.KitType == "Api")
        {
            text.AppendLine(config.KeepOnServerPaths.Contains(AppSettingsTemplate.FileName, StringComparer.OrdinalIgnoreCase)
                ? "When the server already has an appsettings.json, Deploy.sh keeps the server's file and does not copy this one."
                : "This file replaces the server's appsettings.json. Add appsettings.json to the kept-on-server paths on the Azure DevOps page to keep the server's file instead.");
            text.AppendLine("If placeholders are still there after copying, Deploy.sh offers to open the file in nano.");
            text.AppendLine();
        }
    }

    private static string KitTitle(string kitType) => kitType switch
    {
        "Api" => "backend (API)",
        "Frontend" => "frontend",
        _ => "database"
    };

    private static string WebServerName(string webServer) => webServer == "apache" ? "Apache" : "Nginx";
}
