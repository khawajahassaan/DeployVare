using API.Models.Entities;

namespace API.Services.Interfaces;

public interface IIisManagementService
{
    /// <summary>
    /// Stops IIS Application Pool
    /// </summary>
    Task<(bool Success, string Message)> StopAppPoolAsync(ApplicationServer server);

    /// <summary>
    /// Starts IIS Application Pool
    /// </summary>
    Task<(bool Success, string Message)> StartAppPoolAsync(ApplicationServer server);

    /// <summary>
    /// Stops IIS Website
    /// </summary>
    Task<(bool Success, string Message)> StopWebsiteAsync(ApplicationServer server);

    /// <summary>
    /// Starts IIS Website
    /// </summary>
    Task<(bool Success, string Message)> StartWebsiteAsync(ApplicationServer server);

    /// <summary>
    /// Creates a backup of deployment files
    /// </summary>
    Task<(bool Success, string? BackupFileName, string Message)> CreateBackupAsync(ApplicationServer server);

    /// <summary>
    /// Verifies IIS services are running after deployment
    /// </summary>
    Task<(bool Success, string Message)> VerifyDeploymentAsync(ApplicationServer server);

    /// <summary>
    /// Gets the status of App Pool and Website
    /// </summary>
    Task<(bool AppPoolRunning, bool WebsiteRunning, string Message)> GetServiceStatusAsync(ApplicationServer server);

    /// <summary>
    /// Deploys a frontend ZIP file to the remote server by extracting it with Expand-Archive -Force
    /// </summary>
    Task<(bool Success, string Message)> DeployFrontendZipAsync(ApplicationServer server, string localZipPath);

    /// <summary>
    /// Deploys a package ZIP (preserving its folder structure) into the deployment path. When
    /// <paramref name="preserveExistingWebConfig"/> is true, a web.config already on the server is kept.
    /// </summary>
    Task<(bool Success, string Message)> DeployPackageZipAsync(ApplicationServer server, string localZipPath, bool preserveExistingWebConfig);

    /// <summary>Creates a temporary folder on the server for staging a deployment; returns its path.</summary>
    Task<(bool Success, string Message, string? WorkspacePath)> CreateRemoteWorkspaceAsync(ApplicationServer server);

    /// <summary>Uploads a ZIP into a remote workspace and extracts it into a sub-folder; returns the extracted path.</summary>
    Task<(bool Success, string Message, string? ExtractedPath)> UploadAndExtractZipAsync(
        ApplicationServer server, string workspacePath, string localZipPath, string folderName);

    /// <summary>
    /// Copies a staged folder into the deployment path (robocopy, keeping sub-folders). Paths in
    /// <paramref name="keepOnServerPaths"/> (and web.config when preserved) are not overwritten when they exist on the server.
    /// </summary>
    Task<(bool Success, string Message)> PublishStagedFolderAsync(
        ApplicationServer server, string stagedFolderPath, bool preserveExistingWebConfig, IReadOnlyList<string> keepOnServerPaths);

    /// <summary>Best-effort removal of a remote workspace created by <see cref="CreateRemoteWorkspaceAsync"/>.</summary>
    Task RemoveRemoteWorkspaceAsync(ApplicationServer server, string workspacePath);

    /// <summary>
    /// Copies a generated config.js into the deployment path and makes IIS send it with caching disabled.
    /// </summary>
    Task<(bool Success, string Message)> WriteFrontendRuntimeConfigAsync(ApplicationServer server, string localConfigPath);

    /// <summary>
    /// Uploads a licence file and installs it with the application's own --license-install command, which also
    /// marks the server as a licensed installation. Called with IIS stopped, before the site starts.
    ///
    /// The application executable is found in the deployment path rather than named by the caller, the same way
    /// DbMigrator.exe is located, so no product's build details are baked into the deployment service.
    /// </summary>
    Task<(bool Success, string Message)> InstallLicenseAsync(ApplicationServer server, string localLicenseFilePath);

    /// <summary>
    /// Runs --license-status and reports what the server makes of its licence. Used to confirm the activation
    /// before starting IIS, so a site that would refuse every request is never started.
    /// </summary>
    Task<(bool Success, string State, string Message)> CheckLicenseStatusAsync(ApplicationServer server);
}





