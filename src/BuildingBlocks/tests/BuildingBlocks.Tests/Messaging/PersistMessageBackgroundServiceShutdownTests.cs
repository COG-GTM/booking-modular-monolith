#pragma warning disable SA1300

using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class PersistMessageBackgroundServiceShutdownTests
{
    [Fact]
    public async Task cancellation_during_processing_stops_without_error_logging()
    {
        var processor = Substitute.For<IPersistMessageProcessor<MessagingTestModule>>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var anyCancellationToken = Arg.Any<CancellationToken>();
        processor
            .ProcessAllAsync(anyCancellationToken)
            .Returns(async call =>
            {
                entered.TrySetResult();
                var cancellationToken = call.Arg<CancellationToken>();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            });

        var logger = Substitute.For<ILogger<PersistMessageBackgroundService<MessagingTestModule>>>();
        var optionsValue = new PersistMessageOptions { Interval = 0 };
        var options = Options.Create(optionsValue);
        var timeout = TimeSpan.FromSeconds(10);
        await using var services = new ServiceCollection()
            .AddScoped<IPersistMessageProcessor<MessagingTestModule>>(_ => processor)
            .BuildServiceProvider();
        var worker = new PersistMessageBackgroundService<MessagingTestModule>(logger, services, options);

        await worker.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(timeout);
        await worker.StopAsync(CancellationToken.None);

        worker.ExecuteTask!.IsFaulted.Should().BeFalse();
        logger
            .ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
            .Select(call => call.GetArguments()[0])
            .Should()
            .NotContain(LogLevel.Error);
    }
}
