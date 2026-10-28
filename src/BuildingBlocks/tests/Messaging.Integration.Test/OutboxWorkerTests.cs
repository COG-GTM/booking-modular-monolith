using BuildingBlocks.PersistMessageProcessor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Messaging.Integration.Test;

public class OutboxWorkerTests
{
    [Fact]
    public async Task Worker_retries_pending_delivery_after_a_poll_fails()
    {
        var processor = Substitute.For<IPersistMessageProcessor<ProbeEvent>>();
        var recovered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        processor
            .ProcessAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    return Task.FromException(new InvalidOperationException("Broker unavailable"));
                recovered.SetResult();
                return Task.CompletedTask;
            });
        await using var provider = new ServiceCollection().AddScoped(_ => processor).BuildServiceProvider();
        using var worker = new PersistMessageBackgroundService<ProbeEvent>(
            NullLogger<PersistMessageBackgroundService<ProbeEvent>>.Instance,
            provider,
            Options.Create(new PersistMessageOptions { Interval = 1 })
        );
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await recovered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(attempts >= 2);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }
}
