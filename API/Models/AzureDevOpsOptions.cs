namespace API.Models;

/// <summary>
/// Non-secret Azure DevOps integration settings (appsettings "AzureDevOps" section).
/// Credentials are stored per client in the database, never here.
/// </summary>
public class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    /// <summary>Base URL of Azure DevOps Services.</summary>
    public string BaseUrl { get; set; } = "https://dev.azure.com";

    public string ApiVersion { get; set; } = "7.1";

    /// <summary>Timeout for metadata REST calls (projects, repositories, builds).</summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>Timeout for downloading an artifact.</summary>
    public int DownloadTimeoutMinutes { get; set; } = 15;

    /// <summary>Downloads larger than this are aborted.</summary>
    public long MaxArtifactSizeMB { get; set; } = 1024;

    /// <summary>How many recent successful builds are inspected when looking for one with artifacts.</summary>
    public int MaxBuildsToInspect { get; set; } = 20;
}
