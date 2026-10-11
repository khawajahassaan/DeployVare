using System.Security.Cryptography;
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

public class ServerReadinessService : IServerReadinessService
{
    private const string FactsMarker = "DV_FACTS_JSON:";
    private const string DatabaseFactsMarker = "DV_DB_JSON:";
    private const string ResultMarker = "DV_RESULT:";
    private const long MaxCertificateBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex HostNamePattern = new(
        @"^(?=.{1,253}$)[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]{0,61}[A-Za-z0-9])?)*$", RegexOptions.Compiled);
    private static readonly Regex ThumbprintPattern = new(@"^[A-Fa-f0-9]{40}([A-Fa-f0-9]{24})?$", RegexOptions.Compiled);
    private static readonly Regex WindowsPathPattern = new(@"^[A-Za-z]:\\[^<>:""|?*\r\n]*$", RegexOptions.Compiled);

    private readonly DeployVareDbContext _context;
    private readonly ISshService _sshService;
    private readonly IEncryptionService _encryptionService;
    private readonly IInstallerCatalog _installerCatalog;
    private readonly ServerSetupOptions _options;
    private readonly ILogger<ServerReadinessService> _logger;

    public ServerReadinessService(
        DeployVareDbContext context,
        ISshService sshService,
        IEncryptionService encryptionService,
        IInstallerCatalog installerCatalog,
        IOptions<ServerSetupOptions> options,
        ILogger<ServerReadinessService> logger)
    {
        _context = context;
        _sshService = sshService;
        _encryptionService = encryptionService;
        _installerCatalog = installerCatalog;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ServerReadinessReportDto> CheckAsync(int serverId, string checkedBy = "system")
    {
        var server = await GetServerAsync(serverId);
        _logger.LogInformation("ServerReadinessCheckStarted for {ServerName} ({Host})", server.ServerName, server.HostName);

        var facts = await CollectFactsAsync(server);
        var database = server.ServerType == ServerType.Api ? await CheckDatabaseAsync(server, facts) : null;
        var report = ServerReadinessEvaluator.Evaluate(server, facts, _options, _installerCatalog.GetInstallers(), database: database);

        _logger.LogInformation("ServerReadinessCheckCompleted for {ServerName}: ready {IsReady}, failing [{Failing}]",
            server.ServerName, report.IsReady,
            string.Join(", ", report.Items.Where(i => i.Required && i.Status != ReadinessStatus.Pass).Select(i => i.Key)));

        report.CheckedBy = checkedBy;
        await SaveReportAsync(server.Id, report);

        return report;
    }

    public async Task<ServerReadinessReportDto?> GetLastReportAsync(int serverId)
    {
        await GetServerAsync(serverId);

        var saved = await _context.ServerReadinessChecks
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.ApplicationServerId == serverId);

        if (saved == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<ServerReadinessReportDto>(saved.ReportJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Saved readiness report for server {ServerId} could not be read", serverId);
            return null;
        }
    }

    private async Task SaveReportAsync(int serverId, ServerReadinessReportDto report)
    {
        var record = await _context.ServerReadinessChecks.FirstOrDefaultAsync(c => c.ApplicationServerId == serverId);
        if (record == null)
        {
            record = new ServerReadinessCheck { ApplicationServerId = serverId };
            _context.ServerReadinessChecks.Add(record);
        }

        record.CheckedAt = report.CheckedAt;
        record.CheckedBy = report.CheckedBy is { Length: > 100 } by ? by[..100] : report.CheckedBy ?? "system";
        record.IsReady = report.IsReady;
        record.ReportJson = JsonSerializer.Serialize(report);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            // A concurrent check for the same server saved first; the report is still returned to the caller
            _logger.LogWarning(ex, "Readiness report for server {ServerId} was not saved", serverId);
            _context.Entry(record).State = EntityState.Detached;
        }
    }

    public List<InstallerFileDto> GetInstallers() => _installerCatalog.GetInstallers();

    public async Task<ServerSetupActionResultDto> InstallIisAsync(int serverId)
    {
        var server = await GetServerAsync(serverId);
        _logger.LogInformation("ServerSetup InstallIis started on {ServerName}", server.ServerName);

        var result = await RunActionAsync(server, InstallIisScript);
        result.Steps.Insert(0, "Installing IIS (Web Server, WebSockets, management tools)...");
        return Log(server, "InstallIis", result);
    }

    public async Task<ServerSetupActionResultDto> InstallComponentAsync(int serverId, ServerComponent component)
    {
        var server = await GetServerAsync(serverId);
        _logger.LogInformation("ServerSetup InstallComponent {Component} started on {ServerName}", component, server.ServerName);

        // SQL Server has its own install (it needs an sa password and a different setup command line),
        // so the generic silent-installer path here must not run it with the wrong arguments.
        if (component == ServerComponent.SqlServer)
            return Failed("Install SQL Server through the SQL Server setup step, not the generic component installer.");

        var localPath = _installerCatalog.GetInstallerPath(component);
        if (localPath == null)
        {
            var pattern = _installerCatalog.GetInstallers().First(i => i.Component == component.ToString()).Pattern;
            return Failed($"No installer for {component} found. Add a file matching '{pattern}' to the Installers folder.");
        }

        if (component is ServerComponent.HostingBundle or ServerComponent.UrlRewrite)
        {
            var facts = await CollectFactsAsync(server);
            if (!facts.IisInstalled)
                return Failed($"Install IIS before the {component}; it registers itself as an IIS module.");
        }

        var steps = new List<string>();
        var fileName = Path.GetFileName(localPath);
        var remoteDirectory = _options.RemoteInstallersPath.TrimEnd('\\');
        var remotePath = $"{remoteDirectory}\\{fileName}";
        var localSize = new FileInfo(localPath).Length;

        // Reuse an installer already copied to the server by a previous run
        var sizeScript = $"New-Item -Path '{Ps(remoteDirectory)}' -ItemType Directory -Force | Out-Null; "
            + $"if (Test-Path -LiteralPath '{Ps(remotePath)}') {{ (Get-Item -LiteralPath '{Ps(remotePath)}').Length }} else {{ -1 }}";
        var (_, sizeOutput, sizeError) = await RunAsync(server, sizeScript);
        if (!long.TryParse(LastLine(sizeOutput), out var remoteSize))
            return Failed($"Could not prepare the installer folder on the server: {FirstLine(sizeError, sizeOutput)}");

        if (remoteSize == localSize)
        {
            steps.Add($"✓ {fileName} is already on the server");
        }
        else
        {
            steps.Add($"Uploading {fileName} ({localSize / 1024d / 1024d:F0} MB)...");
            var (password, privateKey) = GetCredentials(server);
            var uploaded = await _sshService.UploadFileAsync(server.HostName, server.Port, server.Username, password, privateKey,
                localPath, remotePath.Replace('\\', '/'));
            if (!uploaded)
                return Failed($"Failed to upload {fileName} to the server.", steps);
            steps.Add($"✓ Uploaded {fileName}");
        }

        var postInstall = component switch
        {
            // The ASP.NET Core Module is only picked up after IIS restarts
            ServerComponent.HostingBundle => "net stop was /y | Out-Null; net start w3svc | Out-Null",
            ServerComponent.Redis => RedisPostInstallScript,
            _ => string.Empty
        };

        var preInstall = component == ServerComponent.Redis ? RedisPreInstallScript : string.Empty;
        var msiExtra = component == ServerComponent.Redis ? ", 'INSTALL_SERVICE=1', 'PORT=6379'" : string.Empty;

        var script = InstallComponentScript
            .Replace("__FILE__", Ps(remotePath))
            .Replace("__TIMEOUT_MS__", (Math.Max(1, _options.InstallTimeoutMinutes) * 60_000).ToString())
            .Replace("__TIMEOUT_MIN__", Math.Max(1, _options.InstallTimeoutMinutes).ToString())
            .Replace("__PRE_INSTALL__", preInstall)
            .Replace("__MSI_EXTRA__", msiExtra)
            .Replace("__POST_INSTALL__", postInstall);

        steps.Add($"Running {fileName} silently (this can take several minutes)...");
        var result = await RunActionAsync(server, script);
        result.Steps.InsertRange(0, steps);
        return Log(server, $"InstallComponent {component}", result);
    }

    public async Task<ServerSetupActionResultDto> CreateSiteAsync(int serverId, CreateSiteRequest request)
    {
        var server = await GetServerAsync(serverId);
        var physicalPath = ValidateWindowsPath(request.PhysicalPath);
        var hostName = ValidateHostName(request.HostName);

        _logger.LogInformation("ServerSetup CreateSite {Site} at {Path} on {ServerName}", server.WebsiteName, physicalPath, server.ServerName);

        var script = CreateSiteScript
            .Replace("__SITE__", Ps(server.WebsiteName))
            .Replace("__POOL__", Ps(server.AppPoolName))
            .Replace("__PATH__", Ps(physicalPath))
            .Replace("__HOST__", Ps(hostName));

        var result = await RunActionAsync(server, script);

        if (result.Success && request.UseAsDeploymentPath && !ServerReadinessEvaluator.PathsEqual(server.DeploymentPath, physicalPath))
        {
            server.DeploymentPath = physicalPath;
            server.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            result.Steps.Add($"✓ DeployVare deployment path set to {physicalPath}");
        }

        return Log(server, "CreateSite", result);
    }

    public async Task<ServerSetupActionResultDto> InstallCertificateAsync(int serverId, IFormFile certificateFile, string? password)
    {
        var server = await GetServerAsync(serverId);

        if (certificateFile == null || certificateFile.Length == 0)
            throw new InvalidOperationException("A certificate file is required.");
        if (certificateFile.Length > MaxCertificateBytes)
            throw new InvalidOperationException("The certificate file is larger than 10 MB.");

        var extension = Path.GetExtension(certificateFile.FileName);
        if (!extension.Equals(".pfx", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".p12", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Upload a .pfx or .p12 certificate that includes its private key.");

        _logger.LogInformation("ServerSetup InstallCertificate {FileName} on {ServerName}", Path.GetFileName(certificateFile.FileName), server.ServerName);

        var localDirectory = Path.Combine(Path.GetTempPath(), "DeployVare", "certificates");
        Directory.CreateDirectory(localDirectory);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var localPath = Path.Combine(localDirectory, fileName);
        const string remoteDirectory = @"C:\DeployVare\Certificates";
        var remotePath = $"{remoteDirectory}\\{fileName}";

        try
        {
            await using (var stream = new FileStream(localPath, FileMode.CreateNew, FileAccess.Write))
            {
                await certificateFile.CopyToAsync(stream);
            }

            var (mkdirOk, _, mkdirError) = await RunAsync(server, $"New-Item -Path '{remoteDirectory}' -ItemType Directory -Force | Out-Null");
            if (!mkdirOk)
                return Failed($"Could not prepare the certificate folder on the server: {mkdirError}");

            var (sshPassword, privateKey) = GetCredentials(server);
            if (!await _sshService.UploadFileAsync(server.HostName, server.Port, server.Username, sshPassword, privateKey,
                    localPath, remotePath.Replace('\\', '/')))
                return Failed("Failed to upload the certificate to the server.");

            var passwordExpression = string.IsNullOrEmpty(password)
                ? "(New-Object System.Security.SecureString)"
                : $"(ConvertTo-SecureString -String '{Ps(password)}' -AsPlainText -Force)";

            var script = InstallCertificateScript
                .Replace("__FILE__", Ps(remotePath))
                .Replace("__PASSWORD__", passwordExpression);

            var result = await RunActionAsync(server, script);
            result.Steps.Insert(0, "Importing certificate into LocalMachine\\My...");
            return Log(server, "InstallCertificate", result);
        }
        finally
        {
            try { if (File.Exists(localPath)) File.Delete(localPath); } catch { /* best effort */ }
        }
    }

    public async Task<ServerSetupActionResultDto> ConfigureBindingsAsync(int serverId, ConfigureBindingsRequest request)
    {
        var server = await GetServerAsync(serverId);
        var hostName = ValidateHostName(request.HostName);
        var thumbprint = request.CertificateThumbprint?.Trim().ToUpperInvariant() ?? string.Empty;

        if (!ThumbprintPattern.IsMatch(thumbprint))
            throw new InvalidOperationException("Select a valid certificate.");

        _logger.LogInformation("ServerSetup ConfigureBindings {Site} {HostName} cert {Thumbprint} SNI {Sni} on {ServerName}",
            server.WebsiteName, hostName, thumbprint, request.RequireSni, server.ServerName);

        var script = ConfigureBindingsScript
            .Replace("__SITE__", Ps(server.WebsiteName))
            .Replace("__HOST__", Ps(hostName))
            .Replace("__THUMB__", thumbprint)
            .Replace("__SNI__", request.RequireSni ? "$true" : "$false");

        return Log(server, "ConfigureBindings", await RunActionAsync(server, script));
    }

    public async Task<ServerSetupActionResultDto> SyncDeploymentPathAsync(int serverId)
    {
        var server = await GetServerAsync(serverId);
        var facts = await CollectFactsAsync(server);

        if (!facts.SiteExists || string.IsNullOrWhiteSpace(facts.SitePhysicalPath))
            return Failed($"IIS site '{server.WebsiteName}' does not exist on the server.");

        var previous = server.DeploymentPath;
        server.DeploymentPath = facts.SitePhysicalPath;
        server.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("ServerSetup deployment path for {ServerName} changed from {Previous} to {Current}",
            server.ServerName, previous, server.DeploymentPath);

        return new ServerSetupActionResultDto
        {
            Success = true,
            Message = $"Deployment path updated to {server.DeploymentPath}",
            Steps = { $"✓ Deployment path changed from {previous} to {server.DeploymentPath}" }
        };
    }

    public async Task<ServerSetupActionResultDto> LinkDatabaseInstanceAsync(int serverId, int? databaseInstanceId)
    {
        var server = await GetServerAsync(serverId);
        string message;

        if (databaseInstanceId.HasValue)
        {
            var instance = await _context.DatabaseInstances.FirstOrDefaultAsync(i => i.Id == databaseInstanceId.Value)
                ?? throw new InvalidOperationException("Database instance not found");

            if (instance.ApplicationId != server.ApplicationId)
                throw new InvalidOperationException("The database instance belongs to a different application than this server.");

            message = $"Linked database instance '{instance.InstanceName}'";
        }
        else
        {
            message = "Database instance link removed";
        }

        server.DatabaseInstanceId = databaseInstanceId;
        server.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        _logger.LogInformation("ServerSetup {ServerName}: {Message}", server.ServerName, message);
        return new ServerSetupActionResultDto { Success = true, Message = message, Steps = { $"✓ {message}" } };
    }

    public async Task<ServerSetupActionResultDto> GrantDatabaseAccessAsync(int serverId)
    {
        var server = await GetServerAsync(serverId);
        var facts = await CollectFactsAsync(server);

        if (!facts.AppPoolExists)
            return Failed($"Create application pool '{server.AppPoolName}' first; its identity is the database login.");

        var check = await CheckDatabaseAsync(server, facts);
        if (check.DatabaseInstanceId == null)
            return Failed("Link the database instance this API uses first.");
        if (check.ConfigurationError != null)
            return Failed(check.ConfigurationError);

        var db = check.Facts!;
        if (!db.Connected)
            return Failed($"Could not connect to SQL Server {check.DataSource}: {db.ConnectionError}");
        if (!db.SqlOnSameServer)
            return Failed($"SQL Server {check.DataSource} runs on a different machine. Only same-server database access is supported for now.");
        if (!db.DatabaseExists)
            return Failed($"Database '{check.DatabaseName}' does not exist on {check.DataSource}. Create or restore it first.");

        // Note: We attempt the grant regardless of whether the current connection is a sysadmin,
        // since the grant script will use the current connection and fail gracefully if permissions are insufficient.

        _logger.LogInformation("ServerSetup GrantDatabaseAccess {Login} on {Database} ({DataSource}) for {ServerName}",
            check.LoginName, check.DatabaseName, check.DataSource, server.ServerName);

        // Get the current connected Windows user (deployvare) for also granting access
        var deployVareLogin = db.ConnectedAs ?? string.Empty;

        var script = GrantDatabaseAccessScript
            .Replace("__SQL_FUNCTIONS__", SqlFunctions)
            .Replace("__DATASOURCE__", Ps(check.DataSource!))
            .Replace("__DATABASE__", Ps(check.DatabaseName!))
            .Replace("__LOGIN__", Ps(check.LoginName))
            .Replace("__DEPLOYVARE_LOGIN__", Ps(deployVareLogin));

        var result = await RunActionAsync(server, script);
        var grantMsg = deployVareLogin.Length > 0
            ? $"Granting {check.LoginName} and {deployVareLogin} db_owner on '{check.DatabaseName}' ({check.DataSource})..."
            : $"Granting {check.LoginName} db_owner on '{check.DatabaseName}' ({check.DataSource})...";
        result.Steps.Insert(0, grantMsg);
        return Log(server, "GrantDatabaseAccess", result);
    }

    private async Task<DatabaseAccessCheck> CheckDatabaseAsync(ApplicationServer server, ServerFacts facts)
    {
        var check = new DatabaseAccessCheck
        {
            DatabaseInstanceId = server.DatabaseInstanceId,
            LoginName = ServerReadinessEvaluator.GetDatabaseLoginName(
                server.AppPoolName, facts.AppPoolIdentityType, facts.AppPoolUserName, facts.ComputerName) ?? string.Empty
        };

        if (server.DatabaseInstanceId == null)
            return check;

        var instance = await _context.DatabaseInstances.AsNoTracking().FirstOrDefaultAsync(i => i.Id == server.DatabaseInstanceId.Value);
        if (instance == null)
        {
            check.DatabaseInstanceId = null;
            return check;
        }

        check.InstanceName = instance.InstanceName;

        try
        {
            // Only the server and database names are used; credentials in the connection string are ignored
            var builder = new SqlConnectionStringBuilder(_encryptionService.Decrypt(instance.ConnectionStringEncrypted));
            check.DataSource = builder.DataSource;
            check.DatabaseName = builder.InitialCatalog;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or CryptographicException or KeyNotFoundException)
        {
            _logger.LogWarning("Connection string of database instance {InstanceId} could not be read", instance.Id);
            check.ConfigurationError = $"The connection string of database instance '{instance.InstanceName}' could not be read.";
            return check;
        }

        if (string.IsNullOrWhiteSpace(check.DataSource) || string.IsNullOrWhiteSpace(check.DatabaseName))
        {
            check.ConfigurationError = $"The connection string of database instance '{instance.InstanceName}' has no server or database name.";
            return check;
        }

        if (string.IsNullOrEmpty(check.LoginName))
        {
            check.ConfigurationError = $"Application pool '{server.AppPoolName}' runs as a specific user without a user name, so the database login cannot be determined.";
            return check;
        }

        var script = DatabaseFactsScript
            .Replace("__SQL_FUNCTIONS__", SqlFunctions)
            .Replace("__DATASOURCE__", Ps(check.DataSource))
            .Replace("__DATABASE__", Ps(check.DatabaseName))
            .Replace("__LOGIN__", Ps(check.LoginName));

        var (_, output, error) = await RunAsync(server, script);
        var json = ExtractMarker(output, DatabaseFactsMarker)
            ?? throw new ServerSetupException($"Could not read the database state: {FirstLine(error, output)}");

        try
        {
            check.Facts = JsonSerializer.Deserialize<DatabaseFacts>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Unexpected database readiness output from {ServerName}", server.ServerName);
            throw new ServerSetupException("The server returned an unreadable database report.", ex);
        }

        return check;
    }

    private async Task<ServerFacts> CollectFactsAsync(ApplicationServer server)
    {
        var script = FactsScript
            .Replace("__SITE__", Ps(server.WebsiteName))
            .Replace("__POOL__", Ps(server.AppPoolName));

        var (_, output, error) = await RunAsync(server, script);
        var json = ExtractMarker(output, FactsMarker)
            ?? throw new ServerSetupException($"Could not read the server state: {FirstLine(error, output)}");

        try
        {
            return JsonSerializer.Deserialize<ServerFacts>(json, JsonOptions)
                ?? throw new ServerSetupException("The server returned an empty readiness report.");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Unexpected readiness output from {ServerName}", server.ServerName);
            throw new ServerSetupException("The server returned an unreadable readiness report.", ex);
        }
    }

    private async Task<ServerSetupActionResultDto> RunActionAsync(ApplicationServer server, string script)
    {
        var (_, output, error) = await RunAsync(server, script);
        var json = ExtractMarker(output, ResultMarker);

        if (json == null)
            return Failed(FirstLine(error, output) is { Length: > 0 } detail ? detail : "The server did not return a result.");

        var remote = JsonSerializer.Deserialize<RemoteResult>(json, JsonOptions) ?? new RemoteResult();
        var messages = (remote.Message ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ServerSetupActionResultDto
        {
            Success = remote.Success,
            RebootRequired = remote.RebootRequired,
            Message = remote.Success
                ? (remote.Thumbprint != null ? remote.Thumbprint : messages.LastOrDefault() ?? "Completed")
                : messages.LastOrDefault() ?? "Failed",
            Steps = messages.Select(m => remote.Success ? $"✓ {m}" : $"✗ {m}").ToList()
        };
    }

    private Task<(bool Success, string Output, string Error)> RunAsync(ApplicationServer server, string script)
    {
        var (password, privateKey) = GetCredentials(server);
        return _sshService.ExecutePowerShellCommandAsync(server.HostName, server.Port, server.Username, password, privateKey, script);
    }

    private async Task<ApplicationServer> GetServerAsync(int serverId) =>
        await _context.ApplicationServers.FirstOrDefaultAsync(s => s.Id == serverId)
        ?? throw new KeyNotFoundException("Application server not found");

    private (string? Password, string? PrivateKey) GetCredentials(ApplicationServer server)
    {
        var password = string.IsNullOrEmpty(server.EncryptedPassword) ? null : _encryptionService.Decrypt(server.EncryptedPassword);
        var privateKey = string.IsNullOrEmpty(server.SshPrivateKey) ? null : server.SshPrivateKey;
        return (password, privateKey);
    }

    private ServerSetupActionResultDto Log(ApplicationServer server, string action, ServerSetupActionResultDto result)
    {
        if (result.Success)
            _logger.LogInformation("ServerSetup {Action} succeeded on {ServerName} (reboot required: {Reboot})", action, server.ServerName, result.RebootRequired);
        else
            _logger.LogWarning("ServerSetup {Action} failed on {ServerName}: {Message}", action, server.ServerName, result.Message);
        return result;
    }

    private static ServerSetupActionResultDto Failed(string message, List<string>? steps = null) => new()
    {
        Success = false,
        Message = message,
        Steps = (steps ?? new List<string>()).Append($"✗ {message}").ToList()
    };

    private static string ValidateHostName(string? hostName)
    {
        var value = hostName?.Trim().TrimEnd('.').ToLowerInvariant() ?? string.Empty;
        if (!HostNamePattern.IsMatch(value))
            throw new InvalidOperationException("Enter a valid host name such as dev-api.auditvare.cloud (no http:// or path).");
        return value;
    }

    private static string ValidateWindowsPath(string? path)
    {
        var value = path?.Trim().TrimEnd('\\') ?? string.Empty;
        if (!WindowsPathPattern.IsMatch(value) || value.Split('\\').Any(segment => segment is ".." or "."))
            throw new InvalidOperationException(@"Enter a full Windows folder path such as C:\inetpub\wwwroot\AuditVareDevBackend.");
        return value;
    }

    /// <summary>Escapes a value for a single-quoted PowerShell string.</summary>
    private static string Ps(string value) => value.Replace("'", "''");

    private static string? ExtractMarker(string output, string marker) => output
        .Split('\n')
        .Select(line => line.Trim())
        .LastOrDefault(line => line.StartsWith(marker, StringComparison.Ordinal))?[marker.Length..];

    private static string LastLine(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;

    private static string FirstLine(params string[] texts)
    {
        var line = texts
            .SelectMany(t => (t ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .FirstOrDefault(l => !l.StartsWith("#< CLIXML", StringComparison.Ordinal)) ?? string.Empty;
        return line.Length > 300 ? line[..300] : line;
    }

    private sealed class RemoteResult
    {
        public bool Success { get; set; }
        public bool RebootRequired { get; set; }
        public string? Message { get; set; }
        public string? Thumbprint { get; set; }
    }

    // ---- PowerShell scripts (Windows PowerShell 5.1). Placeholders are replaced with escaped values. ----

    private const string FactsScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'SilentlyContinue'
        $siteName = '__SITE__'
        $poolName = '__POOL__'
        $facts = [ordered]@{}

        $os = Get-CimInstance -ClassName Win32_OperatingSystem
        $facts.os = [string]$os.Caption
        $facts.computerName = $env:COMPUTERNAME
        $facts.isServerOs = [bool](Get-Command Install-WindowsFeature)
        $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
        $facts.isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $facts.rebootPending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')

        $w3svc = Get-Service -Name W3SVC
        $facts.iisInstalled = ($null -ne $w3svc) -and (Test-Path "$env:windir\System32\inetsrv\appcmd.exe")
        $facts.iisRunning = ($null -ne $w3svc) -and ($w3svc.Status -eq 'Running')
        $facts.siteExists = $false
        $facts.sitePhysicalPath = $null
        $facts.siteState = $null
        $facts.appPoolExists = $false
        $facts.appPoolRuntime = $null
        $facts.appPoolIdentityType = $null
        $facts.appPoolUserName = $null
        $facts.aspNetCoreModule = $false
        $facts.urlRewrite = Test-Path 'HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite'
        $facts.bindings = @()

        if ($facts.iisInstalled) {
            Import-Module WebAdministration
            $site = Get-Website | Where-Object { $_.Name -eq $siteName } | Select-Object -First 1
            if ($site) {
                $facts.siteExists = $true
                $facts.sitePhysicalPath = [Environment]::ExpandEnvironmentVariables([string]$site.physicalPath)
                $facts.siteState = [string]$site.State
                $facts.bindings = @(Get-WebBinding -Name $siteName | ForEach-Object {
                    [ordered]@{ protocol = [string]$_.protocol; bindingInformation = [string]$_.bindingInformation; certificateHash = [string]$_.certificateHash }
                })
            }
            $pool = Get-ChildItem IIS:\AppPools | Where-Object { $_.Name -eq $poolName } | Select-Object -First 1
            if ($pool) {
                $facts.appPoolExists = $true
                $facts.appPoolRuntime = [string]$pool.managedRuntimeVersion
                $facts.appPoolIdentityType = [string]$pool.processModel.identityType
                $facts.appPoolUserName = [string]$pool.processModel.userName
            }
            $modules = @(Get-WebGlobalModule | ForEach-Object { $_.Name })
            $facts.aspNetCoreModule = $modules -contains 'AspNetCoreModuleV2'
            $facts.urlRewrite = $facts.urlRewrite -or ($modules -contains 'RewriteModule')
        }

        $facts.aspNetCoreRuntimes = @()
        $facts.sdks = @()
        $dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
        if (Test-Path $dotnet) {
            $facts.aspNetCoreRuntimes = @(& $dotnet --list-runtimes 2>$null | Where-Object { $_ -like 'Microsoft.AspNetCore.App *' } | ForEach-Object { ($_ -split ' ')[1] })
            $facts.sdks = @(& $dotnet --list-sdks 2>$null | ForEach-Object { ($_ -split ' ')[0] })
        }

        # Redis 8 on Windows is Memurai (port 6379). Prefer the Memurai service when both exist;
        # Redis 5 may still be registered and must not hide a successful Memurai install.
        $memurai = Get-CimInstance -ClassName Win32_Service -Filter "Name='Memurai'" | Select-Object -First 1
        $redis = Get-CimInstance -ClassName Win32_Service -Filter "Name='Redis'" | Select-Object -First 1
        $svc = if ($memurai) { $memurai } else { $redis }
        $facts.redisServiceExists = $null -ne $svc
        $facts.redisRunning = ($null -ne $svc) -and ($svc.State -eq 'Running')
        $facts.redisServiceName = if ($svc) { [string]$svc.Name } else { $null }
        $facts.redisVersion = $null
        $facts.redisPortOpen = $false
        if ($svc) {
            $exe = if ($svc.PathName -match '^\s*"([^"]+)"') { $Matches[1] } else { ($svc.PathName -split ' ')[0] }
            if (Test-Path $exe) {
                $versionText = & $exe --version 2>$null | Out-String
                if ($versionText -match 'v=(\d+(\.\d+)+)') { $facts.redisVersion = $Matches[1] }
                elseif ($versionText -match '(\d+\.\d+\.\d+)') { $facts.redisVersion = $Matches[1] }
            }
            $facts.redisPortOpen = $null -ne (Get-NetTCPConnection -LocalPort 6379 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1)
        }

        $facts.certificates = @(Get-ChildItem -Path Cert:\LocalMachine\My | ForEach-Object {
            [ordered]@{ thumbprint = $_.Thumbprint; subject = $_.Subject; friendlyName = $_.FriendlyName; notAfter = $_.NotAfter.ToUniversalTime().ToString('o'); hasPrivateKey = $_.HasPrivateKey }
        })

        Write-Output ('DV_FACTS_JSON:' + (ConvertTo-Json -InputObject $facts -Depth 5 -Compress))
        exit 0
        """;

    private const string InstallIisScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        try {
            $restart = $false
            if (Get-Command Install-WindowsFeature -ErrorAction SilentlyContinue) {
                $result = Install-WindowsFeature -Name Web-Server, Web-WebSockets, Web-Mgmt-Console, Web-Scripting-Tools -IncludeManagementTools
                if (-not $result.Success) { throw 'Install-WindowsFeature reported a failure' }
                $restart = [string]$result.RestartNeeded -eq 'Yes'
            }
            else {
                $result = Enable-WindowsOptionalFeature -Online -All -NoRestart -FeatureName IIS-WebServerRole, IIS-WebServer, IIS-WebSockets, IIS-ManagementConsole, IIS-ManagementScriptingTools
                $restart = [bool]$result.RestartNeeded
            }
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; rebootRequired = $restart; message = 'IIS installed' }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; rebootRequired = $false; message = $_.Exception.Message }))
            exit 1
        }
        """;

    private const string RedisPreInstallScript = """
            $memuraiExe = Join-Path $env:ProgramFiles 'Memurai\memurai.exe'
            $memuraiSvc = Get-Service -Name Memurai -ErrorAction SilentlyContinue
            if ((Test-Path $memuraiExe) -or $memuraiSvc) {
                if ($memuraiSvc) {
                    Set-Service -Name Memurai -StartupType Automatic
                    if ($memuraiSvc.Status -ne 'Running') { Start-Service -Name Memurai }
                }
                Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; rebootRequired = $false; message = 'Memurai is already installed' }))
                exit 0
            }
            # Free port 6379 without uninstalling Redis 5. Memurai then provides Redis 8 on that port.
            Get-Service -Name Redis,Memurai -ErrorAction SilentlyContinue | ForEach-Object {
                if ($_.Status -ne 'Stopped') { Stop-Service -Name $_.Name -Force -ErrorAction SilentlyContinue }
            }
            Start-Sleep -Seconds 2
            netstat -ano | findstr :6379
            foreach ($line in (netstat -ano | findstr :6379)) {
                if ($line -notmatch 'LISTENING') { continue }
                $procId = ($line.Trim() -split '\s+')[-1]
                if ($procId -match '^\d+$' -and [int]$procId -gt 0 -and [int]$procId -ne $PID) {
                    cmd /c ('taskkill /PID ' + $procId + ' /F') | Out-Null
                }
            }
            Start-Sleep -Seconds 1
        """;

    private const string RedisPostInstallScript = """
            $svc = Get-Service -Name Memurai -ErrorAction SilentlyContinue
            if (-not $svc) { $svc = Get-Service -Name Redis -ErrorAction SilentlyContinue }
            if ($svc) {
                Set-Service -Name $svc.Name -StartupType Automatic
                if ($svc.Status -ne 'Running') { Start-Service -Name $svc.Name }
            }
            $redis = Get-Service -Name Redis -ErrorAction SilentlyContinue
            $memurai = Get-Service -Name Memurai -ErrorAction SilentlyContinue
            if ($memurai -and $redis) {
                Stop-Service -Name Redis -Force -ErrorAction SilentlyContinue
                Set-Service -Name Redis -StartupType Manual
            }
        """;

    private const string InstallComponentScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        try {
            $file = '__FILE__'
            __PRE_INSTALL__
            if ($file.EndsWith('.msi', [StringComparison]::OrdinalIgnoreCase)) {
                $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i', ('"' + $file + '"'), '/qn', '/norestart'__MSI_EXTRA__) -PassThru
            }
            else {
                $process = Start-Process -FilePath $file -ArgumentList @('/install', '/quiet', '/norestart') -PassThru
            }
            $null = $process.Handle
            if (-not $process.WaitForExit(__TIMEOUT_MS__)) {
                $process.Kill()
                throw 'Installer did not finish within __TIMEOUT_MIN__ minutes'
            }
            $code = $process.ExitCode
            $reboot = ($code -eq 3010) -or ($code -eq 1641)
            if (($code -ne 0) -and (-not $reboot)) { throw "Installer exited with code $code" }
            __POST_INSTALL__
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; rebootRequired = $reboot; message = "Installed successfully (exit code $code)" }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; rebootRequired = $false; message = $_.Exception.Message }))
            exit 1
        }
        """;

    private const string CreateSiteScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        try {
            Import-Module WebAdministration
            $siteName = '__SITE__'
            $poolName = '__POOL__'
            $path = '__PATH__'
            $hostName = '__HOST__'
            $messages = @()

            if (-not (Test-Path -LiteralPath $path)) {
                New-Item -Path $path -ItemType Directory -Force | Out-Null
                $messages += "Created folder $path"
            }

            if (-not (Get-ChildItem IIS:\AppPools | Where-Object { $_.Name -eq $poolName })) {
                New-WebAppPool -Name $poolName | Out-Null
                Set-ItemProperty -Path "IIS:\AppPools\$poolName" -Name managedRuntimeVersion -Value ''
                $messages += "Created application pool $poolName (No Managed Code)"
            }
            else {
                $messages += "Application pool $poolName already exists"
            }

            $site = Get-Website | Where-Object { $_.Name -eq $siteName } | Select-Object -First 1
            if (-not $site) {
                $ids = @(Get-Website | ForEach-Object { [int]$_.id })
                $id = if ($ids.Count -gt 0) { ($ids | Measure-Object -Maximum).Maximum + 1 } else { 1 }
                New-Website -Name $siteName -Id $id -PhysicalPath $path -ApplicationPool $poolName -Port 80 -HostHeader $hostName | Out-Null
                $messages += "Created site $siteName with binding http://${hostName}:80"
            }
            else {
                Set-ItemProperty -Path "IIS:\Sites\$siteName" -Name applicationPool -Value $poolName
                $messages += "Site $siteName already exists; application pool set to $poolName"
            }

            & icacls $path /grant ('IIS AppPool\' + $poolName + ':(OI)(CI)RX') /Q | Out-Null
            $messages += "Granted read access on $path to IIS AppPool\$poolName"

            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; rebootRequired = $false; message = ($messages -join '|') }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; rebootRequired = $false; message = $_.Exception.Message }))
            exit 1
        }
        """;

    private const string InstallCertificateScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        $file = '__FILE__'
        try {
            $securePassword = __PASSWORD__
            $imported = Import-PfxCertificate -FilePath $file -CertStoreLocation 'Cert:\LocalMachine\My' -Password $securePassword
            $cert = @($imported) | Where-Object { $_.HasPrivateKey } | Select-Object -First 1
            if (-not $cert) { throw 'The certificate file does not contain a private key' }
            $message = 'Imported certificate ' + $cert.Subject + ' (' + $cert.Thumbprint + ')'
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; rebootRequired = $false; message = $message; thumbprint = $cert.Thumbprint }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; rebootRequired = $false; message = $_.Exception.Message }))
            exit 1
        }
        finally {
            Remove-Item -LiteralPath $file -Force -ErrorAction SilentlyContinue
        }
        """;

    private const string ConfigureBindingsScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        try {
            Import-Module WebAdministration
            $siteName = '__SITE__'
            $hostName = '__HOST__'
            $thumbprint = '__THUMB__'
            $requireSni = __SNI__
            $messages = @()

            if (-not (Get-Website | Where-Object { $_.Name -eq $siteName })) { throw "Site $siteName does not exist" }
            $cert = Get-Item -LiteralPath "Cert:\LocalMachine\My\$thumbprint" -ErrorAction SilentlyContinue
            if (-not $cert) { throw "Certificate $thumbprint was not found in LocalMachine\My" }
            if (-not $cert.HasPrivateKey) { throw 'The selected certificate has no private key' }

            if (-not (Get-WebBinding -Name $siteName -Protocol 'http' -Port 80 -HostHeader $hostName)) {
                New-WebBinding -Name $siteName -Protocol 'http' -Port 80 -IPAddress '*' -HostHeader $hostName
                $messages += "Added binding http://${hostName}:80"
            }
            else {
                $messages += "Binding http://${hostName}:80 already exists"
            }

            $sslFlags = if ($requireSni) { 1 } else { 0 }
            $https = Get-WebBinding -Name $siteName -Protocol 'https' -Port 443 -HostHeader $hostName
            if (-not $https) {
                New-WebBinding -Name $siteName -Protocol 'https' -Port 443 -IPAddress '*' -HostHeader $hostName -SslFlags $sslFlags
                $https = Get-WebBinding -Name $siteName -Protocol 'https' -Port 443 -HostHeader $hostName
                $messages += "Added binding https://${hostName}:443"
            }
            else {
                $messages += "Binding https://${hostName}:443 already exists"
            }

            $sslPath = if ($requireSni) { "IIS:\SslBindings\!443!$hostName" } else { 'IIS:\SslBindings\0.0.0.0!443' }
            $existing = Get-Item -Path $sslPath -ErrorAction SilentlyContinue
            if ($existing -and ($existing.Thumbprint -ne $thumbprint)) {
                if (-not $requireSni) {
                    throw "Port 443 is already bound to certificate $($existing.Thumbprint) for all sites without SNI. Enable 'Require SNI' or choose that certificate."
                }
                Remove-Item -Path $sslPath -Force
                $existing = $null
            }
            if (-not $existing) {
                $https.AddSslCertificate($thumbprint, 'My')
                $messages += "Bound certificate $thumbprint to https://${hostName}:443"
            }
            else {
                $messages += "Certificate $thumbprint is already bound to port 443"
            }

            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; rebootRequired = $false; message = ($messages -join '|') }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; rebootRequired = $false; message = $_.Exception.Message }))
            exit 1
        }
        """;

    // Shared SQL helpers: Windows authentication as the SSH user, parameterized @database / @login
    private const string SqlFunctions = """
        function New-DvConnection([string]$catalog) {
            $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
            $builder['Data Source'] = $dataSource
            $builder['Initial Catalog'] = $catalog
            $builder['Integrated Security'] = $true
            $builder['Connect Timeout'] = 15
            $builder['TrustServerCertificate'] = $true
            $connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
            $connection.Open()
            return $connection
        }

        function Invoke-DvQuery($connection, [string]$sql) {
            $command = $connection.CreateCommand()
            $command.CommandText = $sql
            $command.CommandTimeout = 120
            $null = $command.Parameters.AddWithValue('@database', $database)
            $null = $command.Parameters.AddWithValue('@login', $login)
            $reader = $command.ExecuteReader()
            $rows = @()
            try {
                while ($reader.Read()) {
                    $row = @{}
                    for ($i = 0; $i -lt $reader.FieldCount; $i++) { $row[$reader.GetName($i)] = $reader.GetValue($i) }
                    $rows += $row
                }
            }
            finally {
                $reader.Close()
            }
            return ,$rows
        }
        """;

    private const string DatabaseFactsScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        $dataSource = '__DATASOURCE__'
        $database = '__DATABASE__'
        $login = '__LOGIN__'
        $facts = [ordered]@{
            sqlOnSameServer = $false; sqlHost = $null; connected = $false; connectionError = $null; connectedAs = $null
            isSysadmin = $false; databaseExists = $false; loginExists = $false; userExists = $false; isDbOwner = $false
        }

        __SQL_FUNCTIONS__

        $sqlHost = (($dataSource -replace '^(tcp|np|lpc|admin):', '') -split '[\\,]')[0].Trim()
        $facts.sqlHost = $sqlHost
        if (@('.', '(local)', 'localhost', '127.0.0.1', '::1', $env:COMPUTERNAME) -contains $sqlHost) {
            $facts.sqlOnSameServer = $true
        }

        try {
            $master = New-DvConnection 'master'
            try {
                $row = (Invoke-DvQuery $master @'
        SELECT SUSER_SNAME() AS ConnectedAs,
               ISNULL(IS_SRVROLEMEMBER('sysadmin'), 0) AS IsSysadmin,
               CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)) AS MachineName,
               CASE WHEN DB_ID(@database) IS NULL THEN 0 ELSE 1 END AS DatabaseExists,
               CASE WHEN EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login) THEN 1 ELSE 0 END AS LoginExists
        '@)[0]
                $facts.connected = $true
                $facts.connectedAs = [string]$row['ConnectedAs']
                $facts.isSysadmin = [int]$row['IsSysadmin'] -eq 1
                $facts.sqlOnSameServer = ([string]$row['MachineName']) -ieq $env:COMPUTERNAME
                $facts.databaseExists = [int]$row['DatabaseExists'] -eq 1
                $facts.loginExists = [int]$row['LoginExists'] -eq 1
            }
            finally {
                $master.Close()
            }

            if ($facts.databaseExists) {
                $db = New-DvConnection $database
                try {
                    $row = (Invoke-DvQuery $db @'
        SELECT
            CASE WHEN EXISTS (
                SELECT 1 FROM sys.database_principals dp
                JOIN sys.server_principals sp ON dp.sid = sp.sid
                WHERE sp.name = @login) THEN 1 ELSE 0 END AS UserExists,
            CASE WHEN EXISTS (
                SELECT 1 FROM sys.database_role_members rm
                JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
                JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
                JOIN sys.server_principals sp ON m.sid = sp.sid
                WHERE r.name = N'db_owner' AND sp.name = @login) THEN 1 ELSE 0 END AS IsDbOwner
        '@)[0]
                    $facts.userExists = [int]$row['UserExists'] -eq 1
                    $facts.isDbOwner = [int]$row['IsDbOwner'] -eq 1
                }
                finally {
                    $db.Close()
                }
            }
        }
        catch {
            $facts.connectionError = $_.Exception.Message
        }

        Write-Output ('DV_DB_JSON:' + (ConvertTo-Json -InputObject $facts -Compress))
        exit 0
        """;

    private const string GrantDatabaseAccessScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        $dataSource = '__DATASOURCE__'
        $database = '__DATABASE__'
        $login = '__LOGIN__'
        $deployVareLogin = '__DEPLOYVARE_LOGIN__'

        __SQL_FUNCTIONS__

        try {
            $db = New-DvConnection $database
            try {
                $rows = Invoke-DvQuery $db @'
        SET NOCOUNT ON;
        DECLARE @messages TABLE (Id int IDENTITY(1, 1), Message nvarchar(400));
        DECLARE @sql nvarchar(max);
        DECLARE @userName sysname;
        DECLARE @deployVareUser sysname;

        -- Grant access to app pool login
        IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
        BEGIN
            SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' FROM WINDOWS WITH DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME());
            EXEC sys.sp_executesql @sql;
            INSERT @messages (Message) VALUES (N'Created SQL Server login ' + @login);
        END
        ELSE
            INSERT @messages (Message) VALUES (N'SQL Server login ' + @login + N' already exists');

        SELECT @userName = dp.name
        FROM sys.database_principals dp
        JOIN sys.server_principals sp ON dp.sid = sp.sid
        WHERE sp.name = @login;

        IF @userName IS NULL
        BEGIN
            SET @userName = @login;
            SET @sql = N'CREATE USER ' + QUOTENAME(@userName) + N' FOR LOGIN ' + QUOTENAME(@login);
            EXEC sys.sp_executesql @sql;
            INSERT @messages (Message) VALUES (N'Created database user ' + @userName + N' in ' + DB_NAME());
        END
        ELSE
            INSERT @messages (Message) VALUES (N'Database user ' + @userName + N' already exists in ' + DB_NAME());

        IF NOT EXISTS (
            SELECT 1 FROM sys.database_role_members rm
            JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
            JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
            WHERE r.name = N'db_owner' AND m.name = @userName)
        BEGIN
            SET @sql = N'ALTER ROLE [db_owner] ADD MEMBER ' + QUOTENAME(@userName);
            EXEC sys.sp_executesql @sql;
            INSERT @messages (Message) VALUES (N'Added ' + @userName + N' to db_owner');
        END
        ELSE
            INSERT @messages (Message) VALUES (@userName + N' is already a member of db_owner');

        -- Grant access to DeployVare user (current login)
        IF @deployVareLogin IS NOT NULL AND @deployVareLogin != N''
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @deployVareLogin)
            BEGIN
                SET @sql = N'CREATE LOGIN ' + QUOTENAME(@deployVareLogin) + N' FROM WINDOWS WITH DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME());
                EXEC sys.sp_executesql @sql;
                INSERT @messages (Message) VALUES (N'Created SQL Server login ' + @deployVareLogin);
            END
            ELSE
                INSERT @messages (Message) VALUES (N'SQL Server login ' + @deployVareLogin + N' already exists');

            SELECT @deployVareUser = dp.name
            FROM sys.database_principals dp
            JOIN sys.server_principals sp ON dp.sid = sp.sid
            WHERE sp.name = @deployVareLogin;

            IF @deployVareUser IS NULL
            BEGIN
                SET @deployVareUser = @deployVareLogin;
                SET @sql = N'CREATE USER ' + QUOTENAME(@deployVareUser) + N' FOR LOGIN ' + QUOTENAME(@deployVareLogin);
                EXEC sys.sp_executesql @sql;
                INSERT @messages (Message) VALUES (N'Created database user ' + @deployVareUser + N' in ' + DB_NAME());
            END
            ELSE
                INSERT @messages (Message) VALUES (N'Database user ' + @deployVareUser + N' already exists in ' + DB_NAME());

            IF NOT EXISTS (
                SELECT 1 FROM sys.database_role_members rm
                JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
                JOIN sys.database_principals m ON rm.member_principal_id = m.principal_id
                WHERE r.name = N'db_owner' AND m.name = @deployVareUser)
            BEGIN
                SET @sql = N'ALTER ROLE [db_owner] ADD MEMBER ' + QUOTENAME(@deployVareUser);
                EXEC sys.sp_executesql @sql;
                INSERT @messages (Message) VALUES (N'Added ' + @deployVareUser + N' to db_owner');
            END
            ELSE
                INSERT @messages (Message) VALUES (@deployVareUser + N' is already a member of db_owner');
        END

        SELECT Message FROM @messages ORDER BY Id;
        '@
                $messages = @($rows | ForEach-Object { [string]$_['Message'] })
            }
            finally {
                $db.Close()
            }

            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; rebootRequired = $false; message = ($messages -join '|') }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; rebootRequired = $false; message = $_.Exception.Message }))
            exit 1
        }
        """;
}
