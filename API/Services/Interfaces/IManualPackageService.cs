using API.DTOs.Requests;

namespace API.Services.Interfaces;

/// <summary>A prepared manual-deployment ZIP on local disk; the caller streams it and deletes the folder afterwards.</summary>
public sealed record ManualPackageResult(
    string ZipPath,
    string FileName,
    int AppFileCount,
    int SqlFileCount,
    IReadOnlyList<string> ContentFolders)
{
    /// <summary>Every zip of the download: one for a single server, one per tier for two- and three-tier layouts.</summary>
    public IReadOnlyList<ManualPackageFile> Files { get; init; } = new[] { new ManualPackageFile(ZipPath, FileName, "server") };
}

/// <summary>One zip of a download; <paramref name="Tier"/> is server, database, api or frontend.</summary>
public sealed record ManualPackageFile(string Path, string FileName, string Tier);

/// <summary>
/// Builds a downloadable package of a build for deploying by hand, without touching any server.
/// </summary>
public interface IManualPackageService
{
    Task<ManualPackageResult> BuildAsync(DownloadArtifactPackageRequest request, CancellationToken cancellationToken = default);
}
