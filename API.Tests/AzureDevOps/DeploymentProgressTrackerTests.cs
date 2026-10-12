using API.DTOs.Responses;
using API.Services.Implementations;
using API.Utilities;

namespace API.Tests.AzureDevOps;

public class DeploymentProgressTrackerTests
{
    private const string ProgressId = "3f2c1d9e-7b4a-4c1e-9f0a-0123456789ab";

    private static DeploymentProgressTracker StartedTracker()
    {
        var tracker = new DeploymentProgressTracker();
        tracker.Start(ProgressId, 42, DeploymentStages.AzureArtifact);
        return tracker;
    }

    private static DeploymentStageDto Stage(DeploymentProgressDto progress, string key) =>
        progress.Stages.Single(s => s.Key == key);

    [Fact]
    public void Start_ListsEveryStageAsPendingInOrder()
    {
        var progress = StartedTracker().Get(ProgressId)!;

        Assert.Equal(42, progress.DeploymentId);
        Assert.False(progress.IsCompleted);
        Assert.Equal(DeploymentStages.AzureArtifact.Select(s => s.Key), progress.Stages.Select(s => s.Key));
        Assert.All(progress.Stages, s => Assert.Equal(DeploymentStageState.Pending, s.State));
    }

    [Fact]
    public void BeginAndComplete_MoveAStageThroughRunningToDone()
    {
        var tracker = StartedTracker();

        tracker.Begin(ProgressId, DeploymentStages.Download);
        Assert.Equal(DeploymentStageState.Running, Stage(tracker.Get(ProgressId)!, DeploymentStages.Download).State);

        tracker.Complete(ProgressId, DeploymentStages.Download);
        var download = Stage(tracker.Get(ProgressId)!, DeploymentStages.Download);
        Assert.Equal(DeploymentStageState.Succeeded, download.State);
        Assert.NotNull(download.StartedAt);
        Assert.NotNull(download.CompletedAt);
    }

    [Fact]
    public void Complete_CanSkipAStageWithAReason()
    {
        var tracker = StartedTracker();

        tracker.Complete(ProgressId, DeploymentStages.Migrations, DeploymentStageState.Skipped, "Database does not exist");

        var migrations = Stage(tracker.Get(ProgressId)!, DeploymentStages.Migrations);
        Assert.Equal(DeploymentStageState.Skipped, migrations.State);
        Assert.Equal("Database does not exist", migrations.Detail);
    }

    [Fact]
    public void Fail_MarksTheRunningStageFailed()
    {
        var tracker = StartedTracker();
        tracker.Begin(ProgressId, DeploymentStages.Upload);

        tracker.Fail(ProgressId, "Failed to upload package");
        tracker.Finish(ProgressId, succeeded: false);

        var progress = tracker.Get(ProgressId)!;
        Assert.Equal(DeploymentStageState.Failed, Stage(progress, DeploymentStages.Upload).State);
        Assert.Equal("Failed to upload package", Stage(progress, DeploymentStages.Upload).Detail);
        Assert.Equal(DeploymentStageState.Pending, Stage(progress, DeploymentStages.StopServices).State);
        Assert.True(progress.IsCompleted);
        Assert.False(progress.Succeeded);
    }

    [Fact]
    public void Fail_BetweenStages_MarksTheNextPendingStage()
    {
        var tracker = StartedTracker();
        tracker.Begin(ProgressId, DeploymentStages.AzureDevOps);
        tracker.Complete(ProgressId, DeploymentStages.AzureDevOps);

        tracker.Fail(ProgressId, "boom");

        Assert.Equal(DeploymentStageState.Failed, Stage(tracker.Get(ProgressId)!, DeploymentStages.Server).State);
    }

    [Fact]
    public void Get_ReturnsASnapshotThatDoesNotChangeTheTracker()
    {
        var tracker = StartedTracker();

        var snapshot = tracker.Get(ProgressId)!;
        snapshot.Stages[0].State = DeploymentStageState.Failed;

        Assert.Equal(DeploymentStageState.Pending, tracker.Get(ProgressId)!.Stages[0].State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces in it")]
    [InlineData("../../etc/passwd")]
    public void InvalidProgressIds_AreIgnored(string? progressId)
    {
        var tracker = new DeploymentProgressTracker();

        tracker.Start(progressId, 1, DeploymentStages.AzureArtifact);
        tracker.Begin(progressId, DeploymentStages.AzureDevOps);
        tracker.Fail(progressId, "ignored");

        Assert.Null(tracker.Get(progressId));
    }

    [Fact]
    public void Report_UpdatesOnlyARunningStage()
    {
        var tracker = new DeploymentProgressTracker();
        tracker.Start(ProgressId, 0, new[] { ("download", "Download"), ("zip", "Zip") });

        tracker.Report(ProgressId, "download", "ignored while pending");
        Assert.Null(Stage(tracker.Get(ProgressId)!, "download").Detail);

        tracker.Begin(ProgressId, "download");
        tracker.Report(ProgressId, "download", "96.0 MB of 192.0 MB (50%)");
        Assert.Equal("96.0 MB of 192.0 MB (50%)", Stage(tracker.Get(ProgressId)!, "download").Detail);

        tracker.Complete(ProgressId, "download", detail: "192.0 MB");
        tracker.Report(ProgressId, "download", "late report");
        Assert.Equal("192.0 MB", Stage(tracker.Get(ProgressId)!, "download").Detail);
    }

    [Fact]
    public void UnknownProgressId_ReturnsNull()
    {
        Assert.Null(StartedTracker().Get("00000000-0000-0000-0000-000000000000"));
    }
}
