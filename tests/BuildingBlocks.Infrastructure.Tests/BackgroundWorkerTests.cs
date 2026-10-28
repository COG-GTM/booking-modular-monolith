using System.Diagnostics;
using BuildingBlocks.EventStoreDB.BackgroundWorkers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class BackgroundWorkerTests
{
    [Fact]
    public async Task StopCancelsInfinitePerformAndCompletesSuccessfully()
    {
        var performStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new BackgroundWorker(
            NullLogger<BackgroundWorker>.Instance,
            ct =>
            {
                performStarted.TrySetResult();
                return Task.Delay(Timeout.Infinite, ct);
            }
        );
        await worker.StartAsync(CancellationToken.None);
        await performStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopwatch = Stopwatch.StartNew();
        await worker.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Worker took {stopwatch.Elapsed} to stop.");
        Assert.Equal(TaskStatus.RanToCompletion, worker.ExecuteTask?.Status);
    }

    [Fact]
    public async Task PerformFailureIsExposedByExecuteTask()
    {
        using var worker = new BackgroundWorker(
            NullLogger<BackgroundWorker>.Instance,
            async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("perform failed");
            }
        );
        await worker.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteTask!);

        Assert.Equal("perform failed", exception.Message);
    }

    [Fact]
    public async Task StopCancelsTokenPassedToPerform()
    {
        var tokenReceived = new TaskCompletionSource<CancellationToken>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        using var worker = new BackgroundWorker(
            NullLogger<BackgroundWorker>.Instance,
            ct =>
            {
                tokenReceived.TrySetResult(ct);
                return Task.Delay(Timeout.Infinite, ct);
            }
        );
        await worker.StartAsync(CancellationToken.None);
        var token = await tokenReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await worker.StopAsync(CancellationToken.None);

        Assert.True(token.IsCancellationRequested);
    }
}
