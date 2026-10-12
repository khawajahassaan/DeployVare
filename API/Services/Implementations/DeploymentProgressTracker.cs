using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using API.DTOs.Responses;
using API.Services.Interfaces;

namespace API.Services.Implementations;

public sealed class DeploymentProgressTracker : IDeploymentProgressTracker
{
    /// <summary>Finished (or abandoned) progress is kept this long so the page can show the final state.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(2);

    private static readonly Regex ValidId = new("^[A-Za-z0-9-]{8,64}$", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, DeploymentProgressDto> _progress = new(StringComparer.Ordinal);

    public static bool IsValidId(string? progressId) => progressId != null && ValidId.IsMatch(progressId);

    public void Start(string? progressId, int deploymentId, IEnumerable<(string Key, string Label)> stages)
    {
        if (!IsValidId(progressId)) return;

        RemoveExpired();
        _progress[progressId!] = new DeploymentProgressDto
        {
            ProgressId = progressId!,
            DeploymentId = deploymentId,
            StartedAt = DateTime.UtcNow,
            Stages = stages.Select(s => new DeploymentStageDto { Key = s.Key, Label = s.Label }).ToList()
        };
    }

    public void Begin(string? progressId, string stageKey) => Update(progressId, progress =>
    {
        var stage = progress.Stages.FirstOrDefault(s => s.Key == stageKey);
        if (stage == null) return;

        stage.State = DeploymentStageState.Running;
        stage.Detail = null;
        stage.StartedAt = DateTime.UtcNow;
        stage.CompletedAt = null;
    });

    public void Complete(string? progressId, string stageKey, DeploymentStageState state = DeploymentStageState.Succeeded, string? detail = null) =>
        Update(progressId, progress =>
        {
            var stage = progress.Stages.FirstOrDefault(s => s.Key == stageKey);
            if (stage == null) return;

            var now = DateTime.UtcNow;
            stage.State = state;
            stage.Detail = detail;
            stage.StartedAt ??= now;
            stage.CompletedAt = now;
        });

    public void Report(string? progressId, string stageKey, string detail) => Update(progressId, progress =>
    {
        var stage = progress.Stages.FirstOrDefault(s => s.Key == stageKey);
        if (stage is { State: DeploymentStageState.Running }) stage.Detail = detail;
    });

    public void Fail(string? progressId, string errorMessage) => Update(progressId, progress =>
    {
        progress.ErrorMessage = errorMessage;

        // A failure between stages belongs to the stage that was about to run
        var stage = progress.Stages.LastOrDefault(s => s.State == DeploymentStageState.Running)
            ?? progress.Stages.FirstOrDefault(s => s.State == DeploymentStageState.Pending);
        if (stage == null) return;

        stage.State = DeploymentStageState.Failed;
        stage.Detail = errorMessage;
        stage.StartedAt ??= DateTime.UtcNow;
        stage.CompletedAt = DateTime.UtcNow;
    });

    public void Finish(string? progressId, bool succeeded) => Update(progressId, progress =>
    {
        progress.IsCompleted = true;
        progress.Succeeded = succeeded;
        progress.CompletedAt = DateTime.UtcNow;
    });

    public DeploymentProgressDto? Get(string? progressId)
    {
        if (!IsValidId(progressId) || !_progress.TryGetValue(progressId!, out var progress)) return null;

        lock (progress)
        {
            return new DeploymentProgressDto
            {
                ProgressId = progress.ProgressId,
                DeploymentId = progress.DeploymentId,
                StartedAt = progress.StartedAt,
                CompletedAt = progress.CompletedAt,
                IsCompleted = progress.IsCompleted,
                Succeeded = progress.Succeeded,
                ErrorMessage = progress.ErrorMessage,
                Stages = progress.Stages.Select(s => new DeploymentStageDto
                {
                    Key = s.Key,
                    Label = s.Label,
                    State = s.State,
                    Detail = s.Detail,
                    StartedAt = s.StartedAt,
                    CompletedAt = s.CompletedAt
                }).ToList()
            };
        }
    }

    private void Update(string? progressId, Action<DeploymentProgressDto> change)
    {
        if (!IsValidId(progressId) || !_progress.TryGetValue(progressId!, out var progress)) return;

        lock (progress)
        {
            change(progress);
        }
    }

    private void RemoveExpired()
    {
        var cutoff = DateTime.UtcNow - Retention;
        foreach (var (key, progress) in _progress)
        {
            if ((progress.CompletedAt ?? progress.StartedAt) < cutoff)
                _progress.TryRemove(key, out _);
        }
    }
}
