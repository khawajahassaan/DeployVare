using API.DTOs.Requests;
using API.DTOs.Responses;
using API.Models.Enums;

namespace API.Services.Interfaces;

/// <summary>
/// Deploys Azure DevOps builds to Linux servers (Ubuntu/Debian): as downloadable kits for a manual deployment, or
/// one-click over SSH. Both use the same kit and Deploy.sh.
/// </summary>
public interface ILinuxDeploymentService
{
    /// <summary>Latest successful backend and frontend builds of an application and the Linux artifact of each.</summary>
    Task<LinuxBuildsDto> GetBuildsAsync(int applicationId, ArtifactEnvironment environment, CancellationToken cancellationToken = default);

    /// <summary>Logs in over SSH and reports the OS, sudo and installed components.</summary>
    Task<LinuxConnectionTestDto> TestConnectionAsync(LinuxSshTarget target, CancellationToken cancellationToken = default);

    /// <summary>Builds the .tar.gz kits of a manual deployment (one per tier).</summary>
    Task<ManualPackageResult> BuildPackageAsync(LinuxPackageRequest request, CancellationToken cancellationToken = default);

    /// <summary>Builds the kits, uploads them over SFTP and runs Deploy.sh on each server.</summary>
    Task<LinuxDeploymentResultDto> DeployAsync(LinuxDeployRequest request, int userId, CancellationToken cancellationToken = default);
}
