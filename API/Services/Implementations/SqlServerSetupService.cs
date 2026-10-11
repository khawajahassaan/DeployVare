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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace API.Services.Implementations;

public class SqlServerSetupService : ISqlServerSetupService
{
    private const string SqlInfoMarker = "DV_SQL_JSON:";
    private const string DatabaseUsersMarker = "DV_SQL_USERS_JSON:";
    private const string ResultMarker = "DV_RESULT:";
    private const string RemoteTempDirectory = @"C:\DeployVare\Temp";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex InstanceNamePattern = new(@"^[A-Za-z0-9_$#]{1,16}$", RegexOptions.Compiled);
    private static readonly Regex LoginNamePattern = new(@"^[A-Za-z_][A-Za-z0-9_.@-]{0,127}$", RegexOptions.Compiled);

    private readonly DeployVareDbContext _context;
    private readonly ISshService _sshService;
    private readonly IEncryptionService _encryptionService;
    private readonly IInstallerCatalog _installerCatalog;
    private readonly ServerSetupOptions _setupOptions;
    private readonly ILogger<SqlServerSetupService> _logger;

    public SqlServerSetupService(
        DeployVareDbContext context,
        ISshService sshService,
        IEncryptionService encryptionService,
        IInstallerCatalog installerCatalog,
        IOptions<ServerSetupOptions> setupOptions,
        ILogger<SqlServerSetupService> logger)
    {
        _context = context;
        _sshService = sshService;
        _encryptionService = encryptionService;
        _installerCatalog = installerCatalog;
        _setupOptions = setupOptions.Value;
        _logger = logger;
    }

    public async Task<SqlServerInfoDto> GetSqlServerInfoAsync(int serverId, SqlServerConnectRequest? connection)
    {
        var server = await GetServerAsync(serverId);
        return await CollectSqlServerInfoAsync(server, ToCredentials(connection));
    }

    public async Task<List<SqlDatabaseUserDto>> GetDatabaseUsersAsync(int serverId, SqlDatabaseUsersRequest request)
    {
        var server = await GetServerAsync(serverId);
        var instanceName = ValidateInstanceName(request.InstanceName);
        var databaseName = request.DatabaseName?.Trim() ?? string.Empty;

        if (databaseName.Length is 0 or > 128 || databaseName.Any(char.IsControl))
            throw new InvalidOperationException("Select a valid database.");

        var credentials = ToCredentials(request)
            ?? throw new InvalidOperationException("Enter the SQL admin login and password used to connect to SQL Server.");

        string? secretsFile = null;
        try
        {
            if (!credentials.UseWindowsAuthentication)
                secretsFile = await UploadSecretsAsync(server, new { adminLogin = credentials.Login, adminPassword = credentials.Password });

            var script = DatabaseUsersScript
                .Replace("__CONNECTION_SETUP__", ConnectionSetup)
                .Replace("__AUTH_MODE__", AuthMode(credentials))
                .Replace("__SECRETS_FILE__", secretsFile == null ? string.Empty : Ps(secretsFile))
                .Replace("__DATASOURCE__", Ps(DataSourceFor(instanceName)))
                .Replace("__DATABASE__", Ps(databaseName));

            var (password, privateKey) = GetCredentials(server);
            var (_, output, error) = await _sshService.ExecutePowerShellCommandAsync(
                server.HostName, server.Port, server.Username, password, privateKey, script);

            var json = ExtractMarker(output, DatabaseUsersMarker)
                ?? throw new ServerSetupException($"Could not read database users: {FirstLine(error, output)}");

            RemoteUsersResult result;
            try
            {
                result = JsonSerializer.Deserialize<RemoteUsersResult>(json, JsonOptions) ?? new RemoteUsersResult();
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Unexpected database users output from {ServerName}", server.ServerName);
                throw new ServerSetupException("The server returned an unreadable list of database users.", ex);
            }

            if (!result.Success)
                throw new ServerSetupException($"Could not read users of '{databaseName}': {result.Error}");

            return result.Users.Select(u => new SqlDatabaseUserDto
            {
                UserName = u.UserName ?? string.Empty,
                UserType = u.UserType ?? string.Empty,
                LoginName = string.IsNullOrEmpty(u.LoginName) ? null : u.LoginName,
                LoginDisabled = u.LoginDisabled,
                LoginLocked = u.LoginLocked,
                Roles = (u.Roles ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            }).ToList();
        }
        finally
        {
            if (secretsFile != null)
                await RemoveRemoteFileAsync(server, secretsFile);
        }
    }

    public async Task<ServerSetupActionResultDto> CreateSqlLoginAsync(int serverId, CreateSqlLoginRequest request)
    {
        var server = await GetServerAsync(serverId);
        var instanceName = ValidateInstanceName(request.InstanceName);
        var loginName = ValidateLoginName(request.LoginName);
        var databaseName = request.DatabaseName?.Trim() ?? string.Empty;
        var password = request.Password ?? string.Empty;

        if (password.Length is < 8 or > 128)
            throw new InvalidOperationException("The new login's password must be between 8 and 128 characters.");

        var credentials = ToCredentials(request)
            ?? throw new InvalidOperationException("Enter the SQL admin login and password used to connect to SQL Server.");

        if (!credentials.UseWindowsAuthentication && credentials.Login!.Equals(loginName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The new login must be different from the admin login used to connect.");

        var info = await CollectSqlServerInfoAsync(server, credentials);
        if (!info.Installed)
            return Failed("SQL Server is not installed on this server.");

        var instance = info.Instances.FirstOrDefault(i => i.Name.Equals(instanceName, StringComparison.OrdinalIgnoreCase));
        if (instance == null)
            return Failed($"SQL Server instance '{instanceName}' was not found on this server.");
        if (!instance.Running)
            return Failed($"SQL Server instance '{instance.Name}' is not running.");
        if (!instance.Connected)
            return Failed($"Could not connect to SQL Server instance '{instance.Name}' as {credentials}: {instance.ConnectionError}");
        if (!instance.CanManageLogins)
            return Failed($"{instance.ConnectedAs} cannot create logins on '{instance.Name}'. Use a sysadmin or securityadmin login.");

        var database = instance.Databases.FirstOrDefault(d => d.Equals(databaseName, StringComparison.OrdinalIgnoreCase));
        if (database == null)
            return Failed($"Database '{databaseName}' was not found on SQL Server instance '{instance.Name}'.");

        _logger.LogInformation("SqlServerSetup CreateSqlLogin {Login} on {Database} ({Instance}) for {ServerName} as {Credentials}, reset password: {Reset}",
            loginName, database, instance.Name, server.ServerName, credentials, request.ResetPasswordIfExists);

        string? secretsFile = null;
        try
        {
            secretsFile = await UploadSecretsAsync(server, new
            {
                adminLogin = credentials.Login,
                adminPassword = credentials.Password,
                newPassword = password
            });

            var script = CreateSqlLoginScript
                .Replace("__CONNECTION_SETUP__", ConnectionSetup)
                .Replace("__AUTH_MODE__", AuthMode(credentials))
                .Replace("__SECRETS_FILE__", Ps(secretsFile))
                .Replace("__DATASOURCE__", Ps(instance.DataSource))
                .Replace("__DATABASE__", Ps(database))
                .Replace("__LOGIN__", Ps(loginName))
                .Replace("__RESET__", request.ResetPasswordIfExists ? "$true" : "$false");

            var result = await RunActionAsync(server, script);
            result.Steps.Insert(0, $"Creating SQL login {loginName} with db_owner on '{database}' ({instance.Name}) as {credentials}...");

            if (result.Success && !instance.MixedAuthentication)
                result.Steps.Add("⚠ This instance only allows Windows authentication. Enable SQL authentication or the login cannot sign in.");
            if (result.Success)
                result.Steps.Add("The password is not stored by DeployVare. Save it now and use it in the application's connection string.");

            LogResult(server, "CreateSqlLogin", result);
            return result;
        }
        finally
        {
            if (secretsFile != null)
                await RemoveRemoteFileAsync(server, secretsFile);
        }
    }

    public async Task<ServerSetupActionResultDto> EnableSqlAuthenticationAsync(int serverId, string instanceName)
    {
        var server = await GetServerAsync(serverId);
        var name = ValidateInstanceName(instanceName);

        _logger.LogInformation("SqlServerSetup EnableSqlAuthentication on {Instance} for {ServerName}", name, server.ServerName);

        var script = EnableSqlAuthenticationScript.Replace("__INSTANCE__", Ps(name));
        var result = await RunActionAsync(server, script);
        result.Steps.Insert(0, $"Enabling SQL Server and Windows authentication on '{name}' (the SQL Server service restarts)...");

        LogResult(server, "EnableSqlAuthentication", result);
        return result;
    }

    /// <summary>
    /// Connection details from a request: Windows authentication, a validated SQL admin login, or null when
    /// nothing was entered yet (inspect without connecting).
    /// </summary>
    public static SqlAdminCredentials? ToCredentials(SqlServerConnectRequest? connection)
    {
        if (connection == null)
            return null;

        if (connection.UseWindowsAuthentication)
            return SqlAdminCredentials.Windows();

        var hasLogin = !string.IsNullOrWhiteSpace(connection.AdminLogin);
        var hasPassword = !string.IsNullOrEmpty(connection.AdminPassword);

        if (!hasLogin && !hasPassword)
            return null;
        if (!hasLogin || !hasPassword)
            throw new InvalidOperationException("Enter both the SQL admin login and its password.");
        if (connection.AdminPassword!.Length > 128)
            throw new InvalidOperationException("The SQL admin password is too long.");

        return SqlAdminCredentials.Sql(ValidateLoginName(connection.AdminLogin), connection.AdminPassword);
    }

    /// <summary>Local data source for an instance: "." for the default instance, ".\NAME" for named instances.</summary>
    public static string DataSourceFor(string instanceName) =>
        instanceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase) ? "." : $".\\{instanceName}";

    /// <summary>SQL login names: letters, digits and _ . @ - (no Windows DOMAIN\user names).</summary>
    public static string ValidateLoginName(string? loginName)
    {
        var value = loginName?.Trim() ?? string.Empty;
        if (!LoginNamePattern.IsMatch(value))
            throw new InvalidOperationException("Login name must start with a letter or underscore and contain only letters, digits, _ . @ or -.");
        return value;
    }

    public static string ValidateInstanceName(string? instanceName)
    {
        var value = instanceName?.Trim() ?? string.Empty;
        if (!InstanceNamePattern.IsMatch(value))
            throw new InvalidOperationException("Select a valid SQL Server instance.");
        return value;
    }

    private async Task<SqlServerInfoDto> CollectSqlServerInfoAsync(ApplicationServer server, SqlAdminCredentials? credentials)
    {
        string? secretsFile = null;
        try
        {
            if (credentials is { UseWindowsAuthentication: false })
                secretsFile = await UploadSecretsAsync(server, new { adminLogin = credentials.Login, adminPassword = credentials.Password });

            var script = SqlServerInfoScript
                .Replace("__CONNECTION_SETUP__", ConnectionSetup)
                .Replace("__AUTH_MODE__", AuthMode(credentials))
                .Replace("__SECRETS_FILE__", secretsFile == null ? string.Empty : Ps(secretsFile));

            var (password, privateKey) = GetCredentials(server);
            var (_, output, error) = await _sshService.ExecutePowerShellCommandAsync(
                server.HostName, server.Port, server.Username, password, privateKey, script);

            var json = ExtractMarker(output, SqlInfoMarker)
                ?? throw new ServerSetupException($"Could not read SQL Server information: {FirstLine(error, output)}");

            try
            {
                var info = JsonSerializer.Deserialize<SqlServerInfoDto>(json, JsonOptions) ?? new SqlServerInfoDto();
                foreach (var instance in info.Instances)
                    instance.DataSource = DataSourceFor(instance.Name);

                _logger.LogInformation("SqlServerSetup {ServerName}: SQL Server installed {Installed}, instances [{Instances}], connected as {Credentials}",
                    server.ServerName, info.Installed, string.Join(", ", info.Instances.Select(i => $"{i.Name}:{(i.Connected ? "connected" : "not connected")}")),
                    credentials?.ToString() ?? "(not connecting)");
                return info;
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Unexpected SQL Server information from {ServerName}", server.ServerName);
                throw new ServerSetupException("The server returned unreadable SQL Server information.", ex);
            }
        }
        finally
        {
            if (secretsFile != null)
                await RemoveRemoteFileAsync(server, secretsFile);
        }
    }

    /// <summary>
    /// Copies secrets to a temporary file on the server (the scripts read and delete it). Keeps passwords off the
    /// command line, which Windows may record in process audit logs.
    /// </summary>
    public async Task<ServerSetupActionResultDto> InstallSqlServerAsync(int serverId, InstallSqlServerRequest request)
    {
        var server = await GetServerAsync(serverId);
        var instanceName = ValidateInstanceName(request.InstanceName);
        var saPassword = request.SaPassword ?? string.Empty;

        if (saPassword.Length is < 8 or > 128)
            throw new InvalidOperationException("The sa password must be between 8 and 128 characters.");
        if (saPassword.Any(c => char.IsWhiteSpace(c) || c == '"'))
            throw new InvalidOperationException("The sa password must not contain spaces or double-quote characters.");

        var localPath = _installerCatalog.GetInstallerPath(ServerComponent.SqlServer);
        if (localPath == null)
        {
            var pattern = _installerCatalog.GetInstallers().First(i => i.Component == ServerComponent.SqlServer.ToString()).Pattern;
            throw new InvalidOperationException(
                $"No SQL Server installer found. Add SQL2025-SSEI-Expr.exe or SQLEXPR_x64_ENU.exe to the Installers folder (matching '{pattern}').");
        }

        _logger.LogInformation("SqlServerSetup InstallSqlServer {Instance} on {ServerName}", instanceName, server.ServerName);

        var steps = new List<string>();
        var remotePath = await UploadInstallerAsync(server, localPath, steps);

        // The sa password travels in an uploaded file, never in the SSH command text or our logs
        var secretsFile = await UploadSecretsAsync(server, new { saPassword });
        try
        {
            var script = InstallSqlServerScript
                .Replace("__FILE__", Ps(remotePath))
                .Replace("__INSTANCE__", Ps(instanceName))
                .Replace("__SECRETS_FILE__", Ps(secretsFile));

            steps.Add($"Installing SQL Server Express (instance {instanceName}) silently - this can take 10-20 minutes...");
            var result = await RunActionAsync(server, script);
            result.Steps.InsertRange(0, steps);
            LogResult(server, "InstallSqlServer", result);
            return result;
        }
        finally
        {
            await RemoveRemoteFileAsync(server, secretsFile);
        }
    }

    /// <summary>Copies an installer to the server, reusing one already uploaded by a previous run.</summary>
    private async Task<string> UploadInstallerAsync(ApplicationServer server, string localPath, List<string> steps)
    {
        var fileName = Path.GetFileName(localPath);
        var remoteDirectory = _setupOptions.RemoteInstallersPath.TrimEnd('\\');
        var remotePath = $"{remoteDirectory}\\{fileName}";
        var localSize = new FileInfo(localPath).Length;
        var (password, privateKey) = GetCredentials(server);

        var sizeScript = $"New-Item -Path '{Ps(remoteDirectory)}' -ItemType Directory -Force | Out-Null; "
            + $"if (Test-Path -LiteralPath '{Ps(remotePath)}') {{ (Get-Item -LiteralPath '{Ps(remotePath)}').Length }} else {{ -1 }}";
        var (_, sizeOutput, sizeError) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, sizeScript);

        var lastLine = sizeOutput.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        if (!long.TryParse(lastLine, out var remoteSize))
            throw new ServerSetupException($"Could not prepare the installer folder on the server: {FirstLine(sizeError, sizeOutput)}");

        if (remoteSize == localSize)
        {
            steps.Add($"✓ {fileName} is already on the server");
            return remotePath;
        }

        steps.Add($"Uploading {fileName} ({localSize / 1024d / 1024d:F0} MB) - this can take a while...");
        if (!await _sshService.UploadFileAsync(server.HostName, server.Port, server.Username, password, privateKey,
                localPath, remotePath.Replace('\\', '/')))
            throw new ServerSetupException($"Failed to upload {fileName} to the server.");

        steps.Add($"✓ Uploaded {fileName}");
        return remotePath;
    }

    private async Task<string> UploadSecretsAsync(ApplicationServer server, object secrets)
    {
        var (sshPassword, privateKey) = GetCredentials(server);
        var localFile = Path.Combine(Path.GetTempPath(), "DeployVare", "sql", $"{Guid.NewGuid():N}.json");
        var remoteFile = $"{RemoteTempDirectory}\\{Path.GetFileName(localFile)}";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localFile)!);
            await File.WriteAllTextAsync(localFile, JsonSerializer.Serialize(secrets), new UTF8Encoding(false));

            var (mkdirOk, _, mkdirError) = await _sshService.ExecutePowerShellCommandAsync(server.HostName, server.Port, server.Username,
                sshPassword, privateKey, $"New-Item -Path '{RemoteTempDirectory}' -ItemType Directory -Force | Out-Null");
            if (!mkdirOk)
                throw new ServerSetupException($"Could not prepare a temporary folder on the server: {mkdirError}");

            if (!await _sshService.UploadFileAsync(server.HostName, server.Port, server.Username, sshPassword, privateKey,
                    localFile, remoteFile.Replace('\\', '/')))
                throw new ServerSetupException("Could not transfer the credentials to the server.");

            return remoteFile;
        }
        finally
        {
            try { if (File.Exists(localFile)) File.Delete(localFile); } catch { /* best effort */ }
        }
    }

    /// <summary>Best-effort removal of a secrets file the script may not have reached.</summary>
    private async Task RemoveRemoteFileAsync(ApplicationServer server, string remoteFile)
    {
        try
        {
            var (password, privateKey) = GetCredentials(server);
            await _sshService.ExecutePowerShellCommandAsync(server.HostName, server.Port, server.Username, password, privateKey,
                $"Remove-Item -LiteralPath '{Ps(remoteFile)}' -Force -ErrorAction SilentlyContinue");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove temporary credentials file on {ServerName}", server.ServerName);
        }
    }

    private async Task<ServerSetupActionResultDto> RunActionAsync(ApplicationServer server, string script)
    {
        var (password, privateKey) = GetCredentials(server);
        var (_, output, error) = await _sshService.ExecutePowerShellCommandAsync(
            server.HostName, server.Port, server.Username, password, privateKey, script);

        var json = ExtractMarker(output, ResultMarker);
        if (json == null)
            return Failed(FirstLine(error, output) is { Length: > 0 } detail ? detail : "The server did not return a result.");

        var remote = JsonSerializer.Deserialize<RemoteResult>(json, JsonOptions) ?? new RemoteResult();
        var messages = (remote.Message ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new ServerSetupActionResultDto
        {
            Success = remote.Success,
            Message = messages.LastOrDefault() ?? (remote.Success ? "Completed" : "Failed"),
            Steps = messages.Select(m => remote.Success ? $"✓ {m}" : $"✗ {m}").ToList()
        };
    }

    private async Task<ApplicationServer> GetServerAsync(int serverId) =>
        await _context.ApplicationServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == serverId)
        ?? throw new KeyNotFoundException("Application server not found");

    private (string? Password, string? PrivateKey) GetCredentials(ApplicationServer server)
    {
        var password = string.IsNullOrEmpty(server.EncryptedPassword) ? null : _encryptionService.Decrypt(server.EncryptedPassword);
        var privateKey = string.IsNullOrEmpty(server.SshPrivateKey) ? null : server.SshPrivateKey;
        return (password, privateKey);
    }

    private void LogResult(ApplicationServer server, string action, ServerSetupActionResultDto result)
    {
        if (result.Success)
            _logger.LogInformation("SqlServerSetup {Action} succeeded on {ServerName}", action, server.ServerName);
        else
            _logger.LogWarning("SqlServerSetup {Action} failed on {ServerName}: {Message}", action, server.ServerName, result.Message);
    }

    private static string AuthMode(SqlAdminCredentials? credentials) =>
        credentials == null ? "none" : credentials.UseWindowsAuthentication ? "windows" : "sql";

    private static ServerSetupActionResultDto Failed(string message) => new()
    {
        Success = false,
        Message = message,
        Steps = { $"✗ {message}" }
    };

    private static string Ps(string value) => value.Replace("'", "''");

    private static string? ExtractMarker(string output, string marker) => output
        .Split('\n')
        .Select(line => line.Trim())
        .LastOrDefault(line => line.StartsWith(marker, StringComparison.Ordinal))?[marker.Length..];

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
        public string? Message { get; set; }
    }

    private sealed class RemoteUsersResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public List<RemoteUser> Users { get; set; } = new();
    }

    private sealed class RemoteUser
    {
        public string? UserName { get; set; }
        public string? UserType { get; set; }
        public string? LoginName { get; set; }
        public bool LoginDisabled { get; set; }
        public bool LoginLocked { get; set; }
        public string? Roles { get; set; }
    }

    private const string DatabaseUsersScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        $dataSource = '__DATASOURCE__'
        $database = '__DATABASE__'
        $result = [ordered]@{ success = $false; error = $null; users = @() }

        try {
            __CONNECTION_SETUP__

            $connection = New-DvSqlConnection $dataSource $database
            try {
                $command = $connection.CreateCommand()
                $command.CommandTimeout = 60
                $command.CommandText = @'
        SELECT dp.name AS UserName,
               dp.type_desc AS UserType,
               sp.name AS LoginName,
               ISNULL(CAST(sp.is_disabled AS int), 0) AS LoginDisabled,
               ISNULL(CAST(LOGINPROPERTY(sp.name, 'IsLocked') AS int), 0) AS LoginLocked,
               ISNULL(STUFF((
                   SELECT N', ' + r.name
                   FROM sys.database_role_members rm
                   JOIN sys.database_principals r ON rm.role_principal_id = r.principal_id
                   WHERE rm.member_principal_id = dp.principal_id
                   ORDER BY r.name
                   FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 2, N''), N'') AS Roles
        FROM sys.database_principals dp
        LEFT JOIN sys.server_principals sp ON dp.sid = sp.sid
        WHERE dp.type IN ('S', 'U', 'G', 'E', 'X') AND dp.principal_id > 4
        ORDER BY dp.name;
        '@
                $reader = $command.ExecuteReader()
                $users = @()
                while ($reader.Read()) {
                    $users += [ordered]@{
                        userName = [string]$reader['UserName']
                        userType = [string]$reader['UserType']
                        loginName = $(if ($reader['LoginName'] -is [System.DBNull]) { $null } else { [string]$reader['LoginName'] })
                        loginDisabled = [int]$reader['LoginDisabled'] -eq 1
                        loginLocked = [int]$reader['LoginLocked'] -eq 1
                        roles = [string]$reader['Roles']
                    }
                }
                $reader.Close()
                $result.users = $users
                $result.success = $true
            }
            finally {
                $connection.Close()
            }
        }
        catch {
            $result.error = $_.Exception.Message
        }

        Write-Output ('DV_SQL_USERS_JSON:' + (ConvertTo-Json -InputObject $result -Depth 5 -Compress))
        exit 0
        """;

    // ---- PowerShell scripts (Windows PowerShell 5.1). Placeholders are replaced with escaped values. ----

    // Reads the secrets file (deleting it immediately) and defines how SQL connections are opened
    private const string InstallSqlServerScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        try {
            $file = '__FILE__'
            $instance = '__INSTANCE__'
            $secrets = [System.IO.File]::ReadAllText('__SECRETS_FILE__') | ConvertFrom-Json
            $sa = [string]$secrets.saPassword
            $isSsei = [IO.Path]::GetFileName($file) -match '(?i)SSEI'

            $arguments = @(
                '/IACCEPTSQLSERVERLICENSETERMS','/Q','/ACTION=Install','/FEATURES=SQLENGINE',
                ('/INSTANCENAME=' + $instance),'/SECURITYMODE=SQL',('/SAPWD=' + $sa),
                '/SQLSYSADMINACCOUNTS=BUILTIN\Administrators','/TCPENABLED=1','/NPENABLED=1',
                '/SQLSVCSTARTUPTYPE=Automatic','/UpdateEnabled=False'
            )

            if ($isSsei) {
                $arguments += '/HideConsole'
                $p = Start-Process -FilePath $file -ArgumentList $arguments -Wait -PassThru
            }
            else {
                $extract = Join-Path (Split-Path $file -Parent) 'sqlexpr_extracted'
                if (Test-Path $extract) { Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue }

                $ex = Start-Process -FilePath $file -ArgumentList @('/q', ('/x:' + $extract)) -Wait -PassThru
                if ($ex.ExitCode -ne 0) { throw "Extracting the SQL Server installer failed with code $($ex.ExitCode)" }
                $setup = Join-Path $extract 'SETUP.EXE'
                if (-not (Test-Path $setup)) {
                    $setup = Get-ChildItem -Path $extract -Filter 'setup.exe' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
                }
                if (-not $setup) { throw 'setup.exe was not found after extracting the installer.' }
                $p = Start-Process -FilePath $setup -ArgumentList $arguments -Wait -PassThru
                Remove-Item -LiteralPath $extract -Recurse -Force -ErrorAction SilentlyContinue
            }
            $code = $p.ExitCode
            $reboot = ($code -eq 3010)
            if (($code -ne 0) -and (-not $reboot)) {
                throw "SQL Server setup exited with code $code. See Summary.txt under 'C:\Program Files\Microsoft SQL Server\...\Setup Bootstrap\Log' on the server."
            }

            # Let SQL through the firewall on 1433 (harmless when the server is only used locally)
            try { New-NetFirewallRule -DisplayName 'SQL Server 1433 (DeployVare)' -Direction Inbound -Protocol TCP -LocalPort 1433 -Action Allow -ErrorAction SilentlyContinue | Out-Null } catch { }

            Remove-Item -LiteralPath '__SECRETS_FILE__' -Force -ErrorAction SilentlyContinue

            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; rebootRequired = $reboot; message = ('SQL Server Express installed (instance ' + $instance + ')') }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; rebootRequired = $false; message = $_.Exception.Message }))
            exit 1
        }
        """;

    private const string ConnectionSetup = """
        $authMode = '__AUTH_MODE__'
        $secretsFile = '__SECRETS_FILE__'
        $secrets = $null
        if ($secretsFile) {
            try {
                $secrets = [System.IO.File]::ReadAllText($secretsFile) | ConvertFrom-Json
            }
            finally {
                Remove-Item -LiteralPath $secretsFile -Force -ErrorAction SilentlyContinue
            }
        }

        function New-DvSqlConnection([string]$dataSource, [string]$catalog) {
            $builder = New-Object System.Data.SqlClient.SqlConnectionStringBuilder
            $builder['Data Source'] = $dataSource
            $builder['Initial Catalog'] = $catalog
            $builder['Connect Timeout'] = 15
            $builder['TrustServerCertificate'] = $true
            if ($authMode -eq 'sql') {
                $builder['Integrated Security'] = $false
                $builder['User ID'] = [string]$secrets.adminLogin
                $builder['Password'] = [string]$secrets.adminPassword
            }
            else {
                $builder['Integrated Security'] = $true
            }
            $connection = New-Object System.Data.SqlClient.SqlConnection($builder.ConnectionString)
            $connection.Open()
            return $connection
        }
        """;

    private const string SqlServerInfoScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'SilentlyContinue'
        $result = [ordered]@{ installed = $false; instances = @() }

        __CONNECTION_SETUP__

        $names = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL'
        if ($names) {
            $instances = @()
            foreach ($property in @($names.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' })) {
                $instanceName = $property.Name
                $instanceId = [string]$property.Value
                $root = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId"
                $setup = Get-ItemProperty -Path "$root\Setup"
                $loginMode = (Get-ItemProperty -Path "$root\MSSQLServer").LoginMode
                $serviceName = if ($instanceName -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { "MSSQL`$$instanceName" }
                $dataSource = if ($instanceName -eq 'MSSQLSERVER') { '.' } else { ".\$instanceName" }
                $service = Get-Service -Name $serviceName

                $info = [ordered]@{
                    name = $instanceName; serviceName = $serviceName; running = ($null -ne $service) -and ($service.Status -eq 'Running')
                    version = [string]$setup.Version; edition = [string]$setup.Edition; mixedAuthentication = ([int]$loginMode -eq 2)
                    connected = $false; connectionError = $null; connectedAs = $null; canManageLogins = $false; databases = @()
                }

                if ($info.running -and ($authMode -ne 'none')) {
                    try {
                        $connection = New-DvSqlConnection $dataSource 'master'
                        try {
                            $command = $connection.CreateCommand()
                            $command.CommandText = "SELECT SUSER_SNAME(), ISNULL(HAS_PERMS_BY_NAME(NULL, NULL, 'ALTER ANY LOGIN'), 0)"
                            $reader = $command.ExecuteReader()
                            if ($reader.Read()) {
                                $info.connectedAs = [string]$reader.GetValue(0)
                                $info.canManageLogins = [int]$reader.GetValue(1) -eq 1
                            }
                            $reader.Close()

                            $command.CommandText = "SELECT name FROM sys.databases WHERE database_id > 4 AND state_desc = 'ONLINE' ORDER BY name"
                            $reader = $command.ExecuteReader()
                            $databases = @()
                            while ($reader.Read()) { $databases += [string]$reader.GetValue(0) }
                            $reader.Close()

                            $info.databases = $databases
                            $info.connected = $true
                        }
                        finally {
                            $connection.Close()
                        }
                    }
                    catch {
                        $info.connectionError = $_.Exception.Message
                    }
                }

                $instances += $info
            }
            $result.installed = $instances.Count -gt 0
            $result.instances = $instances
        }

        Write-Output ('DV_SQL_JSON:' + (ConvertTo-Json -InputObject $result -Depth 5 -Compress))
        exit 0
        """;

    private const string CreateSqlLoginScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        $dataSource = '__DATASOURCE__'
        $database = '__DATABASE__'
        $login = '__LOGIN__'
        $resetPassword = __RESET__

        try {
            __CONNECTION_SETUP__

            $password = [string]$secrets.newPassword
            $connection = New-DvSqlConnection $dataSource $database

            try {
                $command = $connection.CreateCommand()
                $command.CommandTimeout = 120
                $command.CommandText = @'
        SET NOCOUNT ON;
        DECLARE @messages TABLE (Id int IDENTITY(1, 1), Message nvarchar(400));
        DECLARE @sql nvarchar(max);
        DECLARE @userName sysname;

        IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login)
        BEGIN
            SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''')
                + N', CHECK_POLICY = ON, CHECK_EXPIRATION = OFF, DEFAULT_DATABASE = ' + QUOTENAME(DB_NAME());
            EXEC sys.sp_executesql @sql;
            INSERT @messages (Message) VALUES (N'Created SQL login ' + @login);
        END
        ELSE IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = @login AND type = 'S')
        BEGIN
            RAISERROR(N'A login with this name exists but is not a SQL Server login.', 16, 1);
            RETURN;
        END
        ELSE IF @resetPassword = 1
        BEGIN
            SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') + N' UNLOCK';
            EXEC sys.sp_executesql @sql;
            SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' ENABLE';
            EXEC sys.sp_executesql @sql;
            INSERT @messages (Message) VALUES (N'Login ' + @login + N' already existed: password reset, login unlocked and enabled');
        END
        ELSE
            INSERT @messages (Message) VALUES (N'Login ' + @login + N' already exists; password left unchanged');

        SELECT @userName = dp.name
        FROM sys.database_principals dp
        JOIN sys.server_principals sp ON dp.sid = sp.sid
        WHERE sp.name = @login;

        IF @userName IS NULL
        BEGIN
            IF EXISTS (SELECT 1 FROM sys.database_principals WHERE name = @login AND type = 'S')
            BEGIN
                -- Orphaned user (e.g. database restored from another server): link it to the login
                SET @sql = N'ALTER USER ' + QUOTENAME(@login) + N' WITH LOGIN = ' + QUOTENAME(@login);
                EXEC sys.sp_executesql @sql;
                INSERT @messages (Message) VALUES (N'Linked existing database user ' + @login + N' to the login');
            END
            ELSE
            BEGIN
                SET @sql = N'CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login);
                EXEC sys.sp_executesql @sql;
                INSERT @messages (Message) VALUES (N'Created database user ' + @login + N' in ' + DB_NAME());
            END
            SET @userName = @login;
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

        SELECT Message FROM @messages ORDER BY Id;
        '@
                $null = $command.Parameters.AddWithValue('@login', $login)
                $null = $command.Parameters.AddWithValue('@password', $password)
                $null = $command.Parameters.AddWithValue('@resetPassword', [bool]$resetPassword)

                $reader = $command.ExecuteReader()
                $messages = @()
                while ($reader.Read()) { $messages += [string]$reader.GetValue(0) }
                $reader.Close()
            }
            finally {
                $connection.Close()
            }

            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; message = ($messages -join '|') }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; message = $_.Exception.Message }))
            exit 1
        }
        """;

    private const string EnableSqlAuthenticationScript = """
        $ProgressPreference = 'SilentlyContinue'
        $ErrorActionPreference = 'Stop'
        try {
            $instanceName = '__INSTANCE__'
            $instanceId = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL').$instanceName
            if (-not $instanceId) { throw "SQL Server instance $instanceName was not found" }

            $messages = @()
            Set-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer" -Name LoginMode -Value 2
            $messages += 'Authentication mode set to SQL Server and Windows authentication'

            $serviceName = if ($instanceName -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { "MSSQL`$$instanceName" }
            $agentName = if ($instanceName -eq 'MSSQLSERVER') { 'SQLSERVERAGENT' } else { "SQLAgent`$$instanceName" }
            $agent = Get-Service -Name $agentName -ErrorAction SilentlyContinue
            $agentWasRunning = ($null -ne $agent) -and ($agent.Status -eq 'Running')

            Restart-Service -Name $serviceName -Force
            $messages += "Restarted service $serviceName"

            if ($agentWasRunning) {
                Start-Service -Name $agentName
                $messages += "Started SQL Server Agent ($agentName) again"
            }

            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $true; message = ($messages -join '|') }))
            exit 0
        }
        catch {
            Write-Output ('DV_RESULT:' + (ConvertTo-Json -Compress -InputObject @{ success = $false; message = $_.Exception.Message }))
            exit 1
        }
        """;
}
