using API.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace API.Tests.AzureDevOps;

public class BackgroundOperationsTests : IDisposable
{
    private const string Id = "6f1c2d9e-7b4a-4c1e-9f0a-0123456789ab";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "DeployVareTests", Guid.NewGuid().ToString("N"));

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() { }
    }

    private static BackgroundOperations Create() =>
        new(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new Lifetime(), NullLogger<BackgroundOperations>.Instance);

    private static async Task<BackgroundOperationDto> WaitAsync(BackgroundOperations operations, string id, int userId)
    {
        for (var i = 0; i < 200; i++)
        {
            var operation = operations.Get(id, userId)!;
            if (operation.Status != BackgroundOperationStatus.Running) return operation;
            await Task.Delay(10);
        }
        throw new TimeoutException("operation did not finish");
    }

    [Fact]
    public async Task Start_ReturnsAtOnce_AndKeepsTheResult()
    {
        var operations = Create();
        var gate = new TaskCompletionSource();

        Assert.True(operations.Start(Id, 7, "deployment", async (_, _) => { await gate.Task; return "deployed"; }));
        Assert.Equal(BackgroundOperationStatus.Running, operations.Get(Id, 7)!.Status);

        gate.SetResult();
        var done = await WaitAsync(operations, Id, 7);
        Assert.Equal(BackgroundOperationStatus.Succeeded, done.Status);
        Assert.Equal("deployed", done.Result);
        Assert.NotNull(done.CompletedAt);
    }

    [Fact]
    public async Task Failure_IsReportedWithItsMessage()
    {
        var operations = Create();
        operations.Start(Id, 7, "package", (_, _) => throw new InvalidOperationException("No artifact named 'drop'."));

        var done = await WaitAsync(operations, Id, 7);
        Assert.Equal(BackgroundOperationStatus.Failed, done.Status);
        Assert.Equal("No artifact named 'drop'.", done.Error);
        Assert.Null(done.Result);
    }

    [Fact]
    public void AnotherUser_CannotSeeTheOperation_AndIdsAreNotReused()
    {
        var operations = Create();
        operations.Start(Id, 7, "deployment", async (_, _) => { await Task.Delay(Timeout.Infinite); return null; });

        Assert.Null(operations.Get(Id, 8));
        Assert.False(operations.Start(Id, 7, "deployment", (_, _) => Task.FromResult<object?>(null)));
        Assert.False(operations.Start("bad id!", 7, "deployment", (_, _) => Task.FromResult<object?>(null)));
    }

    [Fact]
    public async Task PackageFile_DownloadsOnlyWithItsToken()
    {
        Directory.CreateDirectory(_root);
        var zip = Path.Combine(_root, "package.zip");
        File.WriteAllText(zip, "zip");
        var operations = Create();

        operations.Start(Id, 7, "package", (_, _) =>
        {
            operations.AttachFile(Id, zip, "AuditVare_manual-deploy.zip");
            return Task.FromResult<object?>("built");
        });
        var done = await WaitAsync(operations, Id, 7);

        Assert.Equal("AuditVare_manual-deploy.zip", done.FileName);
        Assert.Equal(3, done.FileSizeBytes);
        Assert.False(string.IsNullOrEmpty(done.DownloadToken));
        Assert.Equal(zip, operations.GetFile(Id, done.DownloadToken)!.Path);
        Assert.Null(operations.GetFile(Id, "wrong-token"));
        Assert.Null(operations.GetFile(Id, null));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch (IOException) { }
    }
}
