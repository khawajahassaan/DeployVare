using API.Services.Interfaces;
using Renci.SshNet;
using Renci.SshNet.Common;
using System.Net.Sockets;
using System.Text;

namespace API.Services.Implementations;

public class SshService : ISshService
{
    // OpenSSH on Windows runs commands through its default shell. cmd.exe (the default) rejects command lines
    // longer than 8191 characters; PowerShell allows ~32K. Anything longer than this runs from a script file.
    public const int MaxInlineCommandLength = 8000;

    private const string RemoteScriptFolder = @"C:\Windows\Temp";

    private readonly ILogger<SshService> _logger;

    public SshService(ILogger<SshService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(string host, int port, string username, string? password, string? privateKey) =>
        (await TestConnectionWithReasonAsync(host, port, username, password, privateKey)).Success;

    public async Task<(bool Success, string Message)> TestConnectionWithReasonAsync(
        string host, int port, string username, string? password, string? privateKey)
    {
        try
        {
            using var client = CreateSshClient(host, port, username, password, privateKey);
            await Task.Run(() => client.Connect());

            if (client.IsConnected)
            {
                _logger.LogInformation($"Successfully connected to {host}:{port}");
                client.Disconnect();
                return (true, "Connection successful");
            }

            return (false, $"Could not connect to {host}:{port}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to connect to {host}:{port}");
            return (false, DescribeConnectionFailure(ex, host, port, username));
        }
    }

    /// <summary>Turns an SSH connection failure into an actionable message for the person configuring the server.</summary>
    public static string DescribeConnectionFailure(Exception ex, string host, int port, string username)
    {
        var socketError = (ex as SocketException ?? ex.InnerException as SocketException)?.SocketErrorCode;

        if (socketError is SocketError.HostNotFound or SocketError.NoData)
            return $"Host '{host}' could not be found. Enter only the IP address or DNS name; the port goes in SSH Port.";

        if (socketError is SocketError.ConnectionRefused)
            return $"{host}:{port} refused the connection. Nothing is listening on that port; check that OpenSSH Server is running and uses port {port}.";

        if (socketError is SocketError.TimedOut or SocketError.HostUnreachable or SocketError.NetworkUnreachable)
            return $"No response from {host}:{port}. Check that port {port} is open in the server's firewall and any cloud network rules (e.g. Azure NSG).";

        return ex switch
        {
            SshAuthenticationException =>
                $"{host}:{port} is an SSH server, but it rejected the login for '{username}'. Check the username (for a local account use just the name, e.g. admins2) and the password or key.",
            SshOperationTimeoutException =>
                $"No SSH response from {host}:{port}. Either the port is blocked by a firewall, or the service on it is not SSH (for example, a Remote Desktop port).",
            SshConnectionException =>
                $"{host}:{port} did not respond as an SSH server (for example, it may be a Remote Desktop port): {ex.Message}",
            ArgumentException => ex.Message,
            _ => $"Could not connect to {host}:{port}: {ex.Message}"
        };
    }

    public async Task<(bool Success, string Output, string Error)> ExecutePowerShellCommandAsync(
        string host, int port, string username, string? password, string? privateKey, string command)
    {
        try
        {
            using var client = CreateSshClient(host, port, username, password, privateKey);
            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
            {
                return (false, string.Empty, "Failed to connect to server");
            }

            _logger.LogInformation($"Executing PowerShell command on {host}: {command.Substring(0, Math.Min(50, command.Length))}...");

            // Encode the command in Base64 to avoid issues with special characters and shell interpretation
            var commandLine = BuildEncodedCommandLine(command);

            if (commandLine.Length > MaxInlineCommandLength)
            {
                // Too long for the server's shell: upload the script, then run a short command that reads it,
                // deletes it and runs it in the same PowerShell host (no execution policy or shell differences)
                var remoteScriptPath = $@"{RemoteScriptFolder}\dv-{Guid.NewGuid():N}.ps1";
                _logger.LogInformation("Command is {Length} characters; running it from temporary script {Path} on {Host}",
                    commandLine.Length, remoteScriptPath, host);

                if (!await UploadScriptAsync(host, port, username, password, privateKey, command, remoteScriptPath))
                {
                    client.Disconnect();
                    return (false, string.Empty, "Failed to upload the PowerShell script to the server");
                }

                commandLine = BuildEncodedCommandLine(BuildScriptFileRunner(remoteScriptPath));
            }

            var sshCommand = client.CreateCommand(commandLine);
            await Task.Run(() => sshCommand.Execute());

            var output = sshCommand.Result;
            var error = sshCommand.Error;
            var exitStatus = sshCommand.ExitStatus;

            client.Disconnect();

            _logger.LogInformation($"Command completed with exit code: {exitStatus}");

            return (exitStatus == 0, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error executing command on {host}");
            return (false, string.Empty, ex.Message);
        }
    }

    public async Task<bool> UploadFileAsync(
        string host, int port, string username, string? password, string? privateKey,
        string localFilePath, string remoteFilePath)
    {
        try
        {
            using var client = CreateScpClient(host, port, username, password, privateKey);
            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
            {
                _logger.LogError($"Failed to connect to {host} for file upload");
                return false;
            }

            _logger.LogInformation($"Uploading {localFilePath} to {host}:{remoteFilePath}");

            using var fileStream = File.OpenRead(localFilePath);
            await Task.Run(() => client.Upload(fileStream, remoteFilePath));

            client.Disconnect();
            _logger.LogInformation($"Successfully uploaded {localFilePath}");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to upload file {localFilePath} to {host}");
            return false;
        }
    }

    public async Task<bool> UploadFilesAsync(
        string host, int port, string username, string? password, string? privateKey,
        Dictionary<string, string> fileMappings)
    {
        try
        {
            using var client = CreateScpClient(host, port, username, password, privateKey);
            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
            {
                _logger.LogError($"Failed to connect to {host} for file upload");
                return false;
            }

            _logger.LogInformation($"Uploading {fileMappings.Count} files to {host}");

            foreach (var (localPath, remotePath) in fileMappings)
            {
                _logger.LogInformation($"Uploading {Path.GetFileName(localPath)}...");
                using var fileStream = File.OpenRead(localPath);
                await Task.Run(() => client.Upload(fileStream, remotePath));
            }

            client.Disconnect();
            _logger.LogInformation($"Successfully uploaded all {fileMappings.Count} files");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to upload files to {host}");
            return false;
        }
    }

    public async Task<bool> DownloadFileAsync(
        string host, int port, string username, string? password, string? privateKey,
        string remoteFilePath, string localFilePath)
    {
        try
        {
            using var client = CreateScpClient(host, port, username, password, privateKey);
            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
            {
                _logger.LogError($"Failed to connect to {host} for file download");
                return false;
            }

            _logger.LogInformation($"Downloading {remoteFilePath} from {host} to {localFilePath}");

            // Ensure local directory exists
            var directory = Path.GetDirectoryName(localFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStream = File.Create(localFilePath);
            await Task.Run(() => client.Download(remoteFilePath, fileStream));

            client.Disconnect();
            _logger.LogInformation($"Successfully downloaded {remoteFilePath}");

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to download file {remoteFilePath} from {host}");
            return false;
        }
    }

    public async Task<bool> FileExistsAsync(
        string host, int port, string username, string? password, string? privateKey,
        string remoteFilePath)
    {
        try
        {
            var command = $"Test-Path -Path '{remoteFilePath}'";
            var (success, output, _) = await ExecutePowerShellCommandAsync(host, port, username, password, privateKey, command);

            return success && output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to check if file exists: {remoteFilePath}");
            return false;
        }
    }

    public async Task<(bool Success, string Output, string Error)> ExecuteBashCommandAsync(
        string host, int port, string username, string? password, string? privateKey, string command,
        int timeoutSeconds = 300)
    {
        try
        {
            using var client = CreateSshClient(host, port, username, password, privateKey);
            await Task.Run(() => client.Connect());

            if (!client.IsConnected)
            {
                return (false, string.Empty, "Failed to connect to server");
            }

            _logger.LogInformation($"Executing bash command on {host}: {command.Substring(0, Math.Min(50, command.Length))}...");

            var sshCommand = client.CreateCommand(command);
            sshCommand.CommandTimeout = TimeSpan.FromSeconds(timeoutSeconds);

            await Task.Run(() => sshCommand.Execute());

            var output = sshCommand.Result;
            var error = sshCommand.Error;
            var exitStatus = sshCommand.ExitStatus;

            client.Disconnect();

            _logger.LogInformation($"Bash command completed with exit code: {exitStatus}");

            return (exitStatus == 0, output, error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error executing bash command on {host}");
            return (false, string.Empty, ex.Message);
        }
    }

    public static string BuildEncodedCommandLine(string command) =>
        $"powershell -EncodedCommand {Convert.ToBase64String(Encoding.Unicode.GetBytes(command))}";

    /// <summary>
    /// Short PowerShell that runs an uploaded script as if it had been passed inline: the file is read and deleted
    /// first, then dot-sourced as a script block so exit codes, output and error handling behave the same.
    /// </summary>
    public static string BuildScriptFileRunner(string remoteScriptPath) =>
        $"$dvScriptPath = '{remoteScriptPath.Replace("'", "''")}'\n" +
        "$dvScript = [System.IO.File]::ReadAllText($dvScriptPath)\n" +
        "Remove-Item -LiteralPath $dvScriptPath -Force -ErrorAction SilentlyContinue\n" +
        ". ([scriptblock]::Create($dvScript))";

    private async Task<bool> UploadScriptAsync(string host, int port, string username, string? password, string? privateKey,
        string script, string remoteScriptPath)
    {
        try
        {
            using var client = CreateScpClient(host, port, username, password, privateKey);
            await Task.Run(() => client.Connect());

            // UTF-8 with BOM so Windows PowerShell reads non-ASCII characters correctly; kept in memory, never on local disk
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
            using var stream = new MemoryStream(encoding.GetPreamble().Concat(encoding.GetBytes(script)).ToArray());
            await Task.Run(() => client.Upload(stream, remoteScriptPath.Replace("\\", "/")));

            client.Disconnect();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload a PowerShell script to {Host}", host);
            return false;
        }
    }

    private SshClient CreateSshClient(string host, int port, string username, string? password, string? privateKey)
    {
        if (!string.IsNullOrEmpty(privateKey))
        {
            // Use private key authentication
            _logger.LogInformation($"Using private key authentication for {host}:{port}");
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKey));
            var keyFile = new PrivateKeyFile(keyStream);
            return new SshClient(host, port, username, keyFile);
        }
        else if (!string.IsNullOrEmpty(password))
        {
            // Use password authentication
            _logger.LogInformation("Using password authentication for {host}:{port}", host, port);
            return new SshClient(host, port, username, password);
        }
        else
        {
            throw new ArgumentException("Either password or private key must be provided");
        }
    }

    private ScpClient CreateScpClient(string host, int port, string username, string? password, string? privateKey)
    {
        if (!string.IsNullOrEmpty(privateKey))
        {
            // Use private key authentication
            using var keyStream = new MemoryStream(Encoding.UTF8.GetBytes(privateKey));
            var keyFile = new PrivateKeyFile(keyStream);
            return new ScpClient(host, port, username, RemotePathTransformation.DoubleQuote, keyFile);
        }
        else if (!string.IsNullOrEmpty(password))
        {
            // Use password authentication
            return new ScpClient(host, port, username, password, RemotePathTransformation.DoubleQuote);
        }
        else
        {
            throw new ArgumentException("Either password or private key must be provided");
        }
    }
}


