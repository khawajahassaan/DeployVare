using API.DTOs.Responses;

namespace API.Services.Interfaces;

/// <summary>
/// In-memory live progress of running deployments, keyed by a client-generated progress ID.
/// Calls with a missing or invalid ID are ignored, so deployments without progress tracking behave as before.
/// </summary>
public interface IDeploymentProgressTracker
{
    void Start(string? progressId, int deploymentId, IEnumerable<(string Key, string Label)> stages);

    void Begin(string? progressId, string stageKey);

    void Complete(string? progressId, string stageKey, DeploymentStageState state = DeploymentStageState.Succeeded, string? detail = null);

    /// <summary>Updates the detail line of a running stage (e.g. "120 MB of 192 MB") without changing its state.</summary>
    void Report(string? progressId, string stageKey, string detail);

    /// <summary>Marks the running stage (or the next pending one) as failed.</summary>
    void Fail(string? progressId, string errorMessage);

    void Finish(string? progressId, bool succeeded);

    /// <summary>A snapshot of the progress, or null when it is unknown.</summary>
    DeploymentProgressDto? Get(string? progressId);
}
