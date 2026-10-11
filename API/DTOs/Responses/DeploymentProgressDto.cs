namespace API.DTOs.Responses;

public enum DeploymentStageState
{
    Pending,
    Running,
    Succeeded,
    Warning,
    Skipped,
    Failed
}

public sealed class DeploymentStageDto
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public DeploymentStageState State { get; set; } = DeploymentStageState.Pending;

    /// <summary>Reason shown for failed, skipped and warning stages.</summary>
    public string? Detail { get; set; }

    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Live progress of a running deployment, polled by the Deploy App page.</summary>
public sealed class DeploymentProgressDto
{
    public string ProgressId { get; set; } = string.Empty;
    public int DeploymentId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsCompleted { get; set; }
    public bool? Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public List<DeploymentStageDto> Stages { get; set; } = new();
}
