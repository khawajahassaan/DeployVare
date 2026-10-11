namespace API.Services;

public enum AzureDevOpsErrorCode
{
    InvalidConfiguration,
    AuthenticationFailed,
    AccessDenied,
    ProjectNotFound,
    RepositoryNotFound,
    BranchNotFound,
    NoSuccessfulBuild,
    BuildNotFound,
    BuildNotEligible,
    NoArtifacts,
    ArtifactNotFound,
    ArtifactSelectionRequired,
    ArtifactNotDownloadable,
    ArtifactTooLarge,
    DownloadFailed,
    InvalidArtifact,
    Timeout,
    RequestFailed
}

/// <summary>
/// A user-presentable Azure DevOps failure. Messages never contain credentials;
/// technical details belong in server-side logs.
/// </summary>
public class AzureDevOpsException : Exception
{
    public AzureDevOpsErrorCode ErrorCode { get; }

    public AzureDevOpsException(AzureDevOpsErrorCode errorCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    /// <summary>HTTP status the API layer should return for this error.</summary>
    public int ToHttpStatusCode() => ErrorCode switch
    {
        AzureDevOpsErrorCode.InvalidConfiguration => StatusCodes.Status400BadRequest,
        AzureDevOpsErrorCode.ArtifactSelectionRequired => StatusCodes.Status400BadRequest,
        AzureDevOpsErrorCode.BuildNotEligible => StatusCodes.Status400BadRequest,
        AzureDevOpsErrorCode.AuthenticationFailed => StatusCodes.Status502BadGateway,
        AzureDevOpsErrorCode.AccessDenied => StatusCodes.Status502BadGateway,
        AzureDevOpsErrorCode.ProjectNotFound => StatusCodes.Status404NotFound,
        AzureDevOpsErrorCode.RepositoryNotFound => StatusCodes.Status404NotFound,
        AzureDevOpsErrorCode.BranchNotFound => StatusCodes.Status404NotFound,
        AzureDevOpsErrorCode.NoSuccessfulBuild => StatusCodes.Status404NotFound,
        AzureDevOpsErrorCode.BuildNotFound => StatusCodes.Status404NotFound,
        AzureDevOpsErrorCode.NoArtifacts => StatusCodes.Status404NotFound,
        AzureDevOpsErrorCode.ArtifactNotFound => StatusCodes.Status404NotFound,
        AzureDevOpsErrorCode.Timeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status502BadGateway
    };
}
