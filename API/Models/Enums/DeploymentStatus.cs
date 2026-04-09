namespace API.Models.Enums;

public enum DeploymentStatus
{
    Pending,
    InProgress,
    Success,
    PartialSuccess,  // Patches succeeded, but procedures/functions failed
    Failed
}



