namespace API.Services.Interfaces;

public interface ISshService
{
    /// <summary>
    /// Tests SSH connection to a remote server
    /// </summary>
    Task<bool> TestConnectionAsync(string host, int port, string username, string? password, string? privateKey);

    /// <summary>
    /// Executes a PowerShell command on remote Windows server via SSH
    /// </summary>
    Task<(bool Success, string Output, string Error)> ExecutePowerShellCommandAsync(
        string host, int port, string username, string? password, string? privateKey, string command);

    /// <summary>
    /// Uploads a file to remote server via SCP
    /// </summary>
    Task<bool> UploadFileAsync(
        string host, int port, string username, string? password, string? privateKey,
        string localFilePath, string remoteFilePath);

    /// <summary>
    /// Uploads multiple files to remote server via SCP
    /// </summary>
    Task<bool> UploadFilesAsync(
        string host, int port, string username, string? password, string? privateKey,
        Dictionary<string, string> fileMappings); // Local -> Remote path mappings

    /// <summary>
    /// Downloads a file from remote server via SCP
    /// </summary>
    Task<bool> DownloadFileAsync(
        string host, int port, string username, string? password, string? privateKey,
        string remoteFilePath, string localFilePath);

    /// <summary>
    /// Checks if a file exists on remote server
    /// </summary>
    Task<bool> FileExistsAsync(
        string host, int port, string username, string? password, string? privateKey,
        string remoteFilePath);
}





