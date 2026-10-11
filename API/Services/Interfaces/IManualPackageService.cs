using API.DTOs.Requests;

namespace API.Services.Interfaces;

/// <summary>A prepared manual-deployment ZIP on local disk; the caller streams it and deletes the folder afterwards.</summary>
public sealed record ManualPackageResult(
    string ZipPath,
    string FileName,
    int AppFileCount,
    int SqlFileCount,
    IReadOnlyList<string> ContentFolders);

/// <summary>
/// Builds a downloadable package of a build for deploying by hand, without touching any server.
/// </summary>
public interface IManualPackageService
{
    Task<ManualPackageResult> BuildAsync(DownloadArtifactPackageRequest request, CancellationToken cancellationToken = default);
}
