using API.Services.Interfaces;

namespace API.Services.Implementations;

/// <summary>
/// Stores and retrieves files from the local filesystem.
/// The key is expected to be an absolute file path.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    public bool IsCloud => false;

    public Task<string> UploadAsync(string key, Stream data, CancellationToken ct = default)
        // For local storage, files are written directly by callers (e.g. BackupService).
        // Nothing extra to do here; just return the key as-is.
        => Task.FromResult(key);

    public Task<Stream> DownloadAsync(string key, CancellationToken ct = default)
        => Task.FromResult<Stream>(File.OpenRead(key));
}
