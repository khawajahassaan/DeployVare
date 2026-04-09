using API.Services.Interfaces;
using Renci.SshNet;
using System.Text;

namespace API.Services.Implementations;

public class SshService : ISshService
{
    private readonly ILogger<SshService> _logger;

    public SshService(ILogger<SshService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> TestConnectionAsync(string host, int port, string username, string? password, string? privateKey)
    {
        try
        {
            using var client = CreateSshClient(host, port, username, password, privateKey);
            await Task.Run(() => client.Connect());
            
            if (client.IsConnected)
            {
                _logger.LogInformation($"Successfully connected to {host}:{port}");
                client.Disconnect();
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to connect to {host}:{port}");
            return false;
        }
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
            var bytes = System.Text.Encoding.Unicode.GetBytes(command);
            var encodedCommand = Convert.ToBase64String(bytes);
            
            // Execute command via PowerShell with EncodedCommand parameter
            var sshCommand = client.CreateCommand($"powershell -EncodedCommand {encodedCommand}");
            var result = await Task.Run(() => sshCommand.Execute());
            
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
            return new ScpClient(host, port, username, keyFile);
        }
        else if (!string.IsNullOrEmpty(password))
        {
            // Use password authentication
            return new ScpClient(host, port, username, password);
        }
        else
        {
            throw new ArgumentException("Either password or private key must be provided");
        }
    }
}


