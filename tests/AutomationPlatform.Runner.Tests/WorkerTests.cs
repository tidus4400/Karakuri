using AutomationPlatform.Runner;
using Microsoft.Extensions.Logging.Abstractions;

namespace AutomationPlatform.Runner.Tests;

public sealed class WorkerTests
{
    [Fact]
    public async Task ExecuteAsync_StopsWhenCancellationRequested()
    {
        var worker = new TestableWorker();
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => worker.RunForTestAsync(cts.Token));
    }

    [Fact]
    public async Task StartAndStopAsync_DoesNotThrow()
    {
        var worker = new TestableWorker();

        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(20);
        await worker.StopAsync(CancellationToken.None);
    }

    private sealed class TestableWorker() : Worker(NullLogger<Worker>.Instance)
    {
        public Task RunForTestAsync(CancellationToken ct) => ExecuteAsync(ct);
    }
}
