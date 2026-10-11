using System.Globalization;
using API.DTOs.Responses;
using API.Models;
using API.Models.Entities;
using API.Models.Enums;

namespace API.Utilities;

/// <summary>
/// Turns facts collected from a server into the readiness checklist for its server type.
/// API servers: IIS, site/app pool, .NET Hosting Bundle, .NET SDK, Redis, SSL, bindings.
/// Frontend servers: IIS, site/app pool, URL Rewrite, SSL, bindings.
/// </summary>
public static class ServerReadinessEvaluator
{
    public const string DefaultCertificateName = "WMSVC-SHA2";

    public static ServerReadinessReportDto Evaluate(
        ApplicationServer server,
        ServerFacts facts,
        ServerSetupOptions options,
        IReadOnlyCollection<InstallerFileDto> installers,
        DateTime? now = null,
        DatabaseAccessCheck? database = null)
    {
        var utcNow = now ?? DateTime.UtcNow;
        var certificates = MapCertificates(facts, utcNow);
        var bindings = facts.Bindings.Select(MapBinding).ToList();
        var items = new List<ReadinessItemDto>();

        items.Add(facts.IsAdmin
            ? Pass("admin", "Administrator access", "SSH user is a local administrator")
            : Fail("admin", "Administrator access", "The SSH user is not a local administrator. Installing software and changing IIS will fail.", fix: null));

        // IIS
        if (!facts.IisInstalled)
            items.Add(Fail("iis", "IIS installed", "IIS (Web Server role) is not installed.", ReadinessFix.InstallIis));
        else if (!facts.IisRunning)
            items.Add(Warning("iis", "IIS installed", "IIS is installed but the World Wide Web Publishing Service (W3SVC) is not running.", required: true));
        else
            items.Add(Pass("iis", "IIS installed", "IIS is installed and running"));

        // Site and application pool
        if (!facts.IisInstalled)
        {
            items.Add(Skipped("site", $"IIS site '{server.WebsiteName}'", "Install IIS first.", required: true));
            items.Add(Skipped("app-pool", $"Application pool '{server.AppPoolName}'", "Install IIS first.", required: true));
        }
        else
        {
            items.Add(facts.SiteExists
                ? Pass("site", $"IIS site '{server.WebsiteName}'", $"Site exists ({facts.SiteState ?? "unknown state"}) at {facts.SitePhysicalPath}")
                : Fail("site", $"IIS site '{server.WebsiteName}'", "Site does not exist in IIS.", ReadinessFix.CreateSite));

            items.Add(facts.AppPoolExists
                ? Pass("app-pool", $"Application pool '{server.AppPoolName}'", "Application pool exists")
                : Fail("app-pool", $"Application pool '{server.AppPoolName}'", "Application pool does not exist in IIS.", ReadinessFix.CreateSite));

            if (facts.SiteExists && !string.IsNullOrWhiteSpace(facts.SitePhysicalPath)
                && !PathsEqual(facts.SitePhysicalPath, server.DeploymentPath))
            {
                items.Add(Warning("deployment-path", "Deployment path matches site folder",
                    $"DeployVare deploys to '{server.DeploymentPath}' but the IIS site serves '{facts.SitePhysicalPath}'.",
                    required: false, fix: ReadinessFix.UpdateDeploymentPath));
            }
        }

        // Runtime prerequisites
        if (server.ServerType == ServerType.Api)
        {
            var major = options.RequiredDotNetMajorVersion;

            var hostingRuntimes = facts.AspNetCoreRuntimes.Where(v => MajorOf(v) == major).ToList();
            if (hostingRuntimes.Count > 0 && facts.AspNetCoreModule)
                items.Add(Pass("hosting-bundle", $".NET {major} Hosting Bundle", $"ASP.NET Core runtime {string.Join(", ", hostingRuntimes)} and the IIS ASP.NET Core Module are installed"));
            else
                items.Add(Install("hosting-bundle", $".NET {major} Hosting Bundle",
                    hostingRuntimes.Count == 0
                        ? $"ASP.NET Core {major} runtime not found{Found(facts.AspNetCoreRuntimes)}."
                        : "ASP.NET Core Module (AspNetCoreModuleV2) is not registered in IIS. Reinstall the Hosting Bundle after IIS.",
                    ServerComponent.HostingBundle, installers));

            // An existing SDK from the minimum version onward is accepted; otherwise the required SDK is installed
            var minimumSdk = options.MinimumSdkMajorVersion;
            var sdks = facts.Sdks.Where(v => MajorOf(v) >= minimumSdk).ToList();
            items.Add(sdks.Count > 0
                ? Pass("dotnet-sdk", $".NET SDK ({minimumSdk} or newer)", $"SDK {string.Join(", ", sdks)} installed")
                : Install("dotnet-sdk", $".NET SDK ({minimumSdk} or newer)",
                    $"No .NET SDK {minimumSdk} or newer found{Found(facts.Sdks)}; the .NET {major} SDK will be installed.",
                    ServerComponent.DotNetSdk, installers));

            var redisMajor = options.RequiredRedisMajorVersion;
            var memurai = IsMemurai(facts);
            var redisLabel = memurai ? $"Redis {redisMajor} (Memurai)" : $"Redis {redisMajor}";
            if (!facts.RedisServiceExists)
                items.Add(Install("redis", redisLabel, "Memurai / Redis Windows service is not installed.", ServerComponent.Redis, installers));
            else if (!memurai && facts.RedisVersion != null && MajorOf(facts.RedisVersion) != redisMajor)
                items.Add(Install("redis", redisLabel, $"Redis {facts.RedisVersion} is installed; Memurai (Redis {redisMajor}.x on port 6379) is required.", ServerComponent.Redis, installers));
            else if (!facts.RedisRunning)
                items.Add(Warning("redis", redisLabel, $"{facts.RedisServiceName ?? "Redis"} {facts.RedisVersion ?? ""} is installed but the service is not running.".Replace("  ", " "), required: true));
            else
                items.Add(Pass("redis", redisLabel,
                    $"{facts.RedisServiceName ?? "Redis"} {facts.RedisVersion ?? "(version unknown)"} is installed and running"
                    + (facts.RedisPortOpen ? " on port 6379" : string.Empty)));

            items.Add(EvaluateDatabase(database));
        }
        else
        {
            items.Add(!facts.IisInstalled
                ? Skipped("url-rewrite", "IIS URL Rewrite module", "Install IIS first.", required: true)
                : facts.UrlRewrite
                    ? Pass("url-rewrite", "IIS URL Rewrite module", "URL Rewrite module is installed")
                    : Install("url-rewrite", "IIS URL Rewrite module", "URL Rewrite module is not installed (needed for frontend page routing).", ServerComponent.UrlRewrite, installers));

            // Optional: a Dev server may intentionally use the values built into the artifact
            var runtimeSettings = FrontendRuntimeConfig.Parse(server.RuntimeConfigJson);
            items.Add(runtimeSettings.Count > 0
                ? new ReadinessItemDto
                {
                    Key = "runtime-config",
                    Name = "Frontend runtime settings (config.js)",
                    Status = ReadinessStatus.Pass,
                    Detail = $"{runtimeSettings.Count} setting(s) are written to config.js on every deploy",
                    Required = false
                }
                : Warning("runtime-config", "Frontend runtime settings (config.js)",
                    "No runtime settings configured. Deployments use the values built into the artifact (Dev URLs for a Dev build).",
                    required: false, fix: ReadinessFix.ConfigureRuntimeSettings));
        }

        // SSL certificate
        var usable = certificates.Where(c => !c.IsExpired).ToList();
        var defaultCertificate = usable.FirstOrDefault(c => string.Equals(c.FriendlyName, DefaultCertificateName, StringComparison.OrdinalIgnoreCase));
        items.Add(usable.Count > 0
            ? Pass("certificate", "SSL certificate",
                $"{usable.Count} valid certificate(s) with a private key available"
                + (defaultCertificate != null ? $", including the default {DefaultCertificateName}" : $"; default {DefaultCertificateName} not present"))
            : Fail("certificate", "SSL certificate", "No valid certificate with a private key is installed in LocalMachine\\My.", ReadinessFix.ConfigureCertificate));

        // Bindings
        if (!facts.SiteExists)
        {
            items.Add(Skipped("bindings", "Site bindings (80 / 443)", "Create the site first.", required: true));
        }
        else
        {
            var http = bindings.FirstOrDefault(b => b.Protocol.Equals("http", StringComparison.OrdinalIgnoreCase) && b.Port == 80);
            var https = bindings.FirstOrDefault(b => b.Protocol.Equals("https", StringComparison.OrdinalIgnoreCase) && b.Port == 443);
            var httpsCertificateValid = https?.CertificateThumbprint != null
                && usable.Any(c => c.Thumbprint.Equals(https.CertificateThumbprint, StringComparison.OrdinalIgnoreCase));

            if (http != null && https != null && httpsCertificateValid)
            {
                items.Add(Pass("bindings", "Site bindings (80 / 443)",
                    $"http {Describe(http)} and https {Describe(https)} are configured with a valid certificate"));
            }
            else
            {
                var missing = new List<string>();
                if (http == null) missing.Add("no http binding on port 80");
                if (https == null) missing.Add("no https binding on port 443");
                else if (!httpsCertificateValid) missing.Add("the https binding has no valid certificate");
                items.Add(Fail("bindings", "Site bindings (80 / 443)", Capitalize(string.Join("; ", missing)) + ".", ReadinessFix.ConfigureBindings));
            }
        }

        if (facts.RebootPending)
            items.Add(Warning("reboot", "No pending restart", "Windows reports a pending restart. Installations may not take effect until the server restarts.", required: false));

        return new ServerReadinessReportDto
        {
            ServerId = server.Id,
            ServerName = server.ServerName,
            ServerType = server.ServerType.ToString(),
            CheckedAt = utcNow,
            IsReady = items.Where(i => i.Required).All(i => i.Status == ReadinessStatus.Pass),
            RebootPending = facts.RebootPending,
            OperatingSystem = facts.Os,
            SitePhysicalPath = facts.SitePhysicalPath,
            Items = items,
            Certificates = certificates,
            Bindings = bindings,
            Database = server.ServerType == ServerType.Api && database != null ? MapDatabase(database) : null
        };
    }

    /// <summary>
    /// SQL login used by the site's application pool. SQL Server on the same machine sees the pool's own
    /// identity; a separate SQL server would need the computer account or a SQL login instead (future option).
    /// </summary>
    public static string? GetDatabaseLoginName(string appPoolName, string? identityType, string? userName, string? computerName)
    {
        switch (identityType)
        {
            case null or "" or "ApplicationPoolIdentity" or "4":
                return $"IIS APPPOOL\\{appPoolName}";
            case "NetworkService" or "2":
                return @"NT AUTHORITY\NETWORK SERVICE";
            case "LocalService" or "1":
                return @"NT AUTHORITY\LOCAL SERVICE";
            case "LocalSystem" or "0":
                return @"NT AUTHORITY\SYSTEM";
            case "SpecificUser" or "3":
                if (string.IsNullOrWhiteSpace(userName)) return null;
                return userName.StartsWith(@".\", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(computerName)
                    ? $"{computerName}\\{userName[2..]}"
                    : userName;
            default:
                return $"IIS APPPOOL\\{appPoolName}";
        }
    }

    private static ReadinessItemDto EvaluateDatabase(DatabaseAccessCheck? database)
    {
        const string key = "database";
        const string name = "Database access (db_owner)";

        // The database is optional: without a linked instance deployments simply skip database migrations
        if (database?.DatabaseInstanceId == null)
            return Warning(key, name,
                "No database instance is linked. Deployments skip database migrations; select the database this API uses to enable them.",
                required: false, fix: ReadinessFix.ConfigureDatabase);

        if (database.ConfigurationError != null)
            return Fail(key, name, database.ConfigurationError, ReadinessFix.ConfigureDatabase);

        var target = $"'{database.DatabaseName}' on {database.DataSource}";
        var facts = database.Facts;

        if (facts == null)
            return Fail(key, name, $"Database access for {target} could not be checked.", fix: null);

        if (!facts.Connected)
            return Fail(key, name,
                $"Could not connect to SQL Server {database.DataSource} with Windows authentication as the SSH user"
                + (facts.SqlOnSameServer ? string.Empty : $" ('{facts.SqlHost}' does not look like this server)")
                + $": {facts.ConnectionError}", fix: null);

        if (!facts.SqlOnSameServer)
            return Fail(key, name,
                $"SQL Server {database.DataSource} runs on a different machine. Only same-server database access is supported for now.",
                ReadinessFix.ConfigureDatabase);

        if (!facts.IsSysadmin)
            return Fail(key, name,
                $"{facts.ConnectedAs} is not a sysadmin on SQL Server {database.DataSource}, so the login cannot be created. Add it to the sysadmin role.",
                fix: null);

        // A fresh server may not have the database yet; deployments then skip migrations instead of failing
        if (!facts.DatabaseExists)
            return Warning(key, name,
                $"Database {target} does not exist yet. Deployments skip database migrations until it exists; grant access after creating or restoring it.",
                required: false);

        if (facts.LoginExists && facts.UserExists && facts.IsDbOwner)
            return Pass(key, name, $"{database.LoginName} has db_owner on {target}");

        var missing = new List<string>();
        if (!facts.LoginExists) missing.Add("the server login");
        if (!facts.UserExists) missing.Add("the database user");
        if (!facts.IsDbOwner) missing.Add("db_owner membership");

        return Fail(key, name, $"{database.LoginName} is missing {string.Join(", ", missing)} on {target}.", ReadinessFix.GrantDatabaseAccess);
    }

    private static DatabaseAccessDto MapDatabase(DatabaseAccessCheck database) => new()
    {
        DatabaseInstanceId = database.DatabaseInstanceId,
        InstanceName = database.InstanceName,
        DataSource = database.DataSource,
        DatabaseName = database.DatabaseName,
        LoginName = database.LoginName,
        ConfigurationError = database.ConfigurationError,
        SqlOnSameServer = database.Facts?.SqlOnSameServer,
        Connected = database.Facts?.Connected,
        ConnectionError = database.Facts?.ConnectionError,
        ConnectedAs = database.Facts?.ConnectedAs,
        IsSysadmin = database.Facts?.IsSysadmin,
        DatabaseExists = database.Facts?.DatabaseExists,
        LoginExists = database.Facts?.LoginExists,
        UserExists = database.Facts?.UserExists,
        IsDbOwner = database.Facts?.IsDbOwner
    };

    /// <summary>Memurai is the Redis 8 distribution DeployVare installs on Windows (port 6379).</summary>
    public static bool IsMemurai(ServerFacts facts) =>
        (facts.RedisServiceName ?? string.Empty).IndexOf("Memurai", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Major version number of "10.0.12" style versions, or -1.</summary>
    public static int MajorOf(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return -1;
        var head = version.Trim().TrimStart('v', 'V').Split('.')[0];
        return int.TryParse(head, NumberStyles.None, CultureInfo.InvariantCulture, out var major) ? major : -1;
    }

    public static bool PathsEqual(string? left, string? right)
    {
        static string Normalize(string? path) => (path ?? string.Empty).Trim().Replace('/', '\\').TrimEnd('\\');
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static List<ServerCertificateDto> MapCertificates(ServerFacts facts, DateTime utcNow) => facts.Certificates
        .Where(c => c.HasPrivateKey && !string.IsNullOrWhiteSpace(c.Thumbprint))
        .Select(c =>
        {
            DateTime? notAfter = DateTime.TryParse(c.NotAfter, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : null;
            return new ServerCertificateDto
            {
                Thumbprint = c.Thumbprint!.ToUpperInvariant(),
                Subject = c.Subject ?? string.Empty,
                FriendlyName = string.IsNullOrWhiteSpace(c.FriendlyName) ? null : c.FriendlyName,
                NotAfter = notAfter,
                IsExpired = notAfter.HasValue && notAfter.Value < utcNow
            };
        })
        .OrderBy(c => c.IsExpired)
        .ThenByDescending(c => c.NotAfter)
        .ToList();

    private static SiteBindingDto MapBinding(ServerBindingFact binding)
    {
        // bindingInformation is "ip:port:host"
        var parts = (binding.BindingInformation ?? string.Empty).Split(':');
        return new SiteBindingDto
        {
            Protocol = binding.Protocol ?? string.Empty,
            BindingInformation = binding.BindingInformation ?? string.Empty,
            Port = parts.Length >= 2 && int.TryParse(parts[1], out var port) ? port : null,
            HostName = parts.Length >= 3 && parts[2].Length > 0 ? parts[2] : null,
            CertificateThumbprint = string.IsNullOrWhiteSpace(binding.CertificateHash) ? null : binding.CertificateHash.ToUpperInvariant()
        };
    }

    private static string Describe(SiteBindingDto binding) => $"{binding.HostName ?? "*"}:{binding.Port}";

    private static string Found(List<string> versions) => versions.Count > 0 ? $" (found {string.Join(", ", versions)})" : string.Empty;

    private static string Capitalize(string value) => value.Length == 0 ? value : char.ToUpperInvariant(value[0]) + value[1..];

    private static ReadinessItemDto Pass(string key, string name, string detail) =>
        new() { Key = key, Name = name, Status = ReadinessStatus.Pass, Detail = detail, Required = true };

    private static ReadinessItemDto Fail(string key, string name, string detail, string? fix) =>
        new() { Key = key, Name = name, Status = ReadinessStatus.Fail, Detail = detail, Required = true, Fix = fix };

    private static ReadinessItemDto Warning(string key, string name, string detail, bool required, string? fix = null) =>
        new() { Key = key, Name = name, Status = ReadinessStatus.Warning, Detail = detail, Required = required, Fix = fix };

    private static ReadinessItemDto Skipped(string key, string name, string detail, bool required) =>
        new() { Key = key, Name = name, Status = ReadinessStatus.Skipped, Detail = detail, Required = required };

    private static ReadinessItemDto Install(string key, string name, string detail, ServerComponent component, IReadOnlyCollection<InstallerFileDto> installers)
    {
        var installer = installers.FirstOrDefault(i => i.Component == component.ToString());
        var available = installer?.Available == true;
        return new ReadinessItemDto
        {
            Key = key,
            Name = name,
            Status = ReadinessStatus.Fail,
            Detail = available
                ? $"{detail} Installer available: {installer!.FileName}."
                : $"{detail} No installer found — add a file matching '{installer?.Pattern}' to the Installers folder.",
            Required = true,
            Fix = ReadinessFix.InstallComponent,
            Component = component.ToString(),
            InstallerAvailable = available
        };
    }
}
