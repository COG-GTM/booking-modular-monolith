using System.Collections.Concurrent;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;
using ValidationException = BuildingBlocks.Exception.ValidationException;

namespace Messaging.Integration.Test;

public class RetryPipelineTests
{
    [Fact]
    public async Task Transient_failures_are_retried_through_the_inbox_filter_with_the_same_message_id()
    {
        await using var pipeline = await RetryPipeline.Start();
        var message = new ProbeEvent(Guid.NewGuid(), FailAttempts: 2);
        var messageId = Guid.NewGuid();

        await pipeline.Harness.Bus.Publish(message, x => x.MessageId = messageId);

        Assert.True(await pipeline.Harness.Consumed.Any<ProbeEvent>(x => x.Exception is null));
        await BrokerFixture.Eventually(() => Task.FromResult(pipeline.State.Completed.ContainsKey(message.EventId)));
        Assert.Equal(3, pipeline.State.Attempts[message.EventId]);
        Assert.Equal(3, pipeline.InboxCalls.Count(x => x == (messageId, "/svc-probe")));
        Assert.False(await pipeline.Harness.Published.Any<Fault<ProbeEvent>>());
    }

    [Fact]
    public async Task Validation_failures_are_not_retried_and_fault_immediately()
    {
        await using var pipeline = await RetryPipeline.Start();
        var message = new ValidationProbeEvent(Guid.NewGuid());

        await pipeline.Harness.Bus.Publish(message);

        Assert.True(
            await pipeline.Harness.Published.Any<Fault<ValidationProbeEvent>>(x =>
                x.Context.Message.Message.EventId == message.EventId
            )
        );
        Assert.Equal(1, pipeline.State.Attempts[message.EventId]);
        Assert.Single(pipeline.InboxCalls, x => x.Consumer == "/svc-validation-probe");
    }

    private sealed class RetryPipeline : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        private RetryPipeline(ServiceProvider provider, ProbeState state, ConcurrentQueue<(Guid, string)> calls)
        {
            _provider = provider;
            State = state;
            InboxCalls = calls;
        }

        public ProbeState State { get; }
        public ConcurrentQueue<(Guid MessageId, string Consumer)> InboxCalls { get; }
        public ITestHarness Harness => _provider.GetRequiredService<ITestHarness>();

        public static async Task<RetryPipeline> Start()
        {
            var env = Substitute.For<IWebHostEnvironment>();
            env.EnvironmentName.Returns("test");
            var state = new ProbeState();
            var calls = new ConcurrentQueue<(Guid, string)>();
            var processor = Substitute.For<IPersistMessageProcessor>();
            processor
                .ExecuteInboxAsync(
                    Arg.Any<Guid>(),
                    Arg.Any<string>(),
                    Arg.Any<Func<Task>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    calls.Enqueue((call.ArgAt<Guid>(0), call.ArgAt<string>(1)));
                    return call.ArgAt<Func<Task>>(2)();
                });
            var services = new ServiceCollection().AddLogging();
            services.AddSingleton(state);
            services.AddScoped(_ => processor);
            services.AddCustomMassTransit(
                env,
                new ConfigurationBuilder().Build(),
                "svc",
                typeof(ProbeConsumer).Assembly
            );
            var provider = services.BuildServiceProvider();
            await provider.GetRequiredService<ITestHarness>().Start();
            return new RetryPipeline(provider, state, calls);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            await _provider.DisposeAsync();
        }
    }
}

public record ValidationProbeEvent(Guid EventId);

public class ValidationProbeConsumer(ProbeState state) : IConsumer<ValidationProbeEvent>
{
    public Task Consume(ConsumeContext<ValidationProbeEvent> context)
    {
        state.Attempts.AddOrUpdate(context.Message.EventId, 1, (_, n) => n + 1);
        throw new ValidationException("Invalid probe payload");
    }
}
