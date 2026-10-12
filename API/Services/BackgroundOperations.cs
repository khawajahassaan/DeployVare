using System.Collections.Concurrent;
using System.Security.Cryptography;
using API.Services.Implementations;

namespace API.Services;

public enum BackgroundOperationStatus
{
    Running,
    Succeeded,
    Failed
}

/// <summary>What the page sees: status, error, and the result (a deployment result, or a package to download).</summary>
public sealed class BackgroundOperationDto
{
    public string OperationId { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public BackgroundOperationStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public object? Result { get; set; }

    /// <summary>Package operations: the finished zip, downloadable with the token below (no login header needed).</summary>
    public string? FileName { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? DownloadToken { get; set; }
}

/// <summary>A finished package file, resolved by operation ID and download token.</summary>
public sealed record BackgroundOperationFile(string Path, string FileName);

/// <summary>
/// Runs long actions (package builds, deployments) outside the HTTP request that started them. The request returns
/// at once with the operation ID; the page follows the stages through the progress tracker and reads the result
/// here. Nothing then waits minutes for a response, so proxies with a response timeout (Cloudflare: 100 s) and
/// closed tabs no longer cut an action off half-way: it is cancelled only when the API itself shuts down.
/// </summary>
public interface IBackgroundOperations
{
    /// <summary>Starts <paramref name="work"/> in its own DI scope. False when the ID is invalid or already in use.</summary>
    bool Start(string operationId, int userId, string kind, Func<IServiceProvider, CancellationToken, Task<object?>> work);

    /// <summary>The operation, or null when it is unknown or belongs to another user.</summary>
    BackgroundOperationDto? Get(string operationId, int userId);

    /// <summary>Package file for a valid download token, or null.</summary>
    BackgroundOperationFile? GetFile(string operationId, string? token);

    /// <summary>Called by package work when the zip is ready, so the page can download it.</summary>
    void AttachFile(string operationId, string path, string fileName);
}

public sealed class BackgroundOperations : IBackgroundOperations
{
    /// <summary>Finished operations (and their package files) are kept this long, so a download can be retried.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(2);

    private sealed class Operation
    {
        public required string Id { get; init; }
        public required int UserId { get; init; }
        public required string Kind { get; init; }
        public DateTime StartedAt { get; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
        public BackgroundOperationStatus Status { get; set; } = BackgroundOperationStatus.Running;
        public string? Error { get; set; }
        public object? Result { get; set; }
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
        public string? DownloadToken { get; set; }
    }

    private readonly ConcurrentDictionary<string, Operation> _operations = new(StringComparer.Ordinal);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundOperations> _logger;
    private readonly CancellationToken _stopping;

    public BackgroundOperations(IServiceScopeFactory scopeFactory, IHostApplicationLifetime lifetime, ILogger<BackgroundOperations> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _stopping = lifetime.ApplicationStopping;
    }

    public bool Start(string operationId, int userId, string kind, Func<IServiceProvider, CancellationToken, Task<object?>> work)
    {
        if (!DeploymentProgressTracker.IsValidId(operationId)) return false;
        RemoveExpired();

        var operation = new Operation { Id = operationId, UserId = userId, Kind = kind };
        if (!_operations.TryAdd(operationId, operation)) return false;

        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var result = await work(scope.ServiceProvider, _stopping);
                lock (operation)
                {
                    operation.Result = result;
                    operation.Status = BackgroundOperationStatus.Succeeded;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background {Kind} {OperationId} failed", kind, operationId);
                lock (operation)
                {
                    operation.Error = ex is OperationCanceledException ? "Cancelled because DeployVare was shutting down." : ex.Message;
                    operation.Status = BackgroundOperationStatus.Failed;
                }
            }
            finally
            {
                lock (operation) operation.CompletedAt = DateTime.UtcNow;
            }
        });

        return true;
    }

    public BackgroundOperationDto? Get(string operationId, int userId)
    {
        if (!_operations.TryGetValue(operationId, out var operation) || operation.UserId != userId) return null;
        lock (operation)
        {
            return new BackgroundOperationDto
            {
                OperationId = operation.Id,
                Kind = operation.Kind,
                Status = operation.Status,
                Error = operation.Error,
                StartedAt = operation.StartedAt,
                CompletedAt = operation.CompletedAt,
                Result = operation.Status == BackgroundOperationStatus.Succeeded ? operation.Result : null,
                FileName = operation.FileName,
                FileSizeBytes = operation.FileSize,
                DownloadToken = operation.Status == BackgroundOperationStatus.Succeeded ? operation.DownloadToken : null
            };
        }
    }

    public BackgroundOperationFile? GetFile(string operationId, string? token)
    {
        if (string.IsNullOrEmpty(token) || !_operations.TryGetValue(operationId, out var operation)) return null;
        lock (operation)
        {
            if (operation.Status != BackgroundOperationStatus.Succeeded || operation.DownloadToken == null) return null;
            var expected = System.Text.Encoding.ASCII.GetBytes(operation.DownloadToken);
            var given = System.Text.Encoding.ASCII.GetBytes(token);
            if (!CryptographicOperations.FixedTimeEquals(expected, given)) return null;
            return operation.FilePath != null && File.Exists(operation.FilePath)
                ? new BackgroundOperationFile(operation.FilePath, operation.FileName ?? Path.GetFileName(operation.FilePath))
                : null;
        }
    }

    public void AttachFile(string operationId, string path, string fileName)
    {
        if (!_operations.TryGetValue(operationId, out var operation)) return;
        lock (operation)
        {
            operation.FilePath = path;
            operation.FileName = fileName;
            operation.FileSize = new FileInfo(path).Length;
            // 256-bit random, URL-safe: the download link works without the login header, so the token is the key
            operation.DownloadToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }

    private void RemoveExpired()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var (key, operation) in _operations)
        {
            if (operation.CompletedAt is not DateTime done || done >= cutoff) continue;
            if (!_operations.TryRemove(key, out _)) continue;
            DeleteFile(operation.FilePath);
        }
    }

    private void DeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            // A package lives alone in its own workspace folder
            var folder = Path.GetDirectoryName(path);
            if (folder != null && Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete expired package {Path}", path);
        }
    }
}
