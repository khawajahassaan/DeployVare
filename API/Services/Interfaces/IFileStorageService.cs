namespace API.Services.Interfaces;

/// <summary>
/// Abstraction over file storage backends (local filesystem or Backblaze B2 / S3-compatible).
/// </summary>
public interface IFileStorageService
{
    /// <summary>True when files are stored in cloud object storage (B2/S3).</summary>
    bool IsCloud { get; }

    /// <summary>
    /// Upload a stream under the given key and return the stored key/path.
    /// </summary>
    Task<string> UploadAsync(string key, Stream data, CancellationToken ct = default);

    /// <summary>
    /// Return a readable stream for the file at <paramref name="key"/>.
    /// </summary>
    Task<Stream> DownloadAsync(string key, CancellationToken ct = default);
}
