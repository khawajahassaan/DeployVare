namespace API.Models.Enums;

public enum ApiDeploymentStatus
{
    Pending,
    StoppingServices,
    CreatingBackup,
    UploadingFiles,
    StartingServices,
    Verifying,
    Success,
    Failed,
    RolledBack
}



