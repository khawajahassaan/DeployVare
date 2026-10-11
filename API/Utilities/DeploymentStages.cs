namespace API.Utilities;

/// <summary>
/// Stages of an Azure DevOps artifact deployment, in execution order. The Deploy App page shows the same labels
/// before a deployment starts (keep web/app/app-deploy/page.tsx AZURE_DEPLOY_STEPS in sync).
/// </summary>
public static class DeploymentStages
{
    public const string AzureDevOps = "azure";
    public const string Server = "server";
    public const string Build = "build";
    public const string Download = "download";
    public const string Package = "package";
    public const string DatabaseCheck = "database-check";
    public const string Upload = "upload";
    public const string Migrations = "migrations";
    public const string StopServices = "stop";
    public const string Backup = "backup";
    public const string DeployFiles = "deploy";
    public const string InstallLicense = "licence";
    public const string VerifyLicense = "licence-verify";
    public const string StartServices = "start";
    public const string Verify = "verify";

    public static readonly IReadOnlyList<(string Key, string Label)> AzureArtifact = new[]
    {
        (AzureDevOps, "Connect to Azure DevOps"),
        (Server, "Connect to server and check readiness"),
        (Build, "Verify build"),
        (Download, "Download artifact"),
        (Package, "Extract and validate package"),
        (DatabaseCheck, "Check database"),
        (Upload, "Upload package to server"),
        (Migrations, "Run database migrations"),
        (StopServices, "Stop IIS services"),
        (Backup, "Back up existing files"),
        (DeployFiles, "Deploy files"),
        // Both run before IIS starts: a licensed application refuses to start without a valid activation, so
        // installing and checking it afterwards would mean starting a site that cannot serve.
        (InstallLicense, "Install licence"),
        (VerifyLicense, "Verify activation"),
        (StartServices, "Start IIS services"),
        (Verify, "Verify deployment"),
    };
}
