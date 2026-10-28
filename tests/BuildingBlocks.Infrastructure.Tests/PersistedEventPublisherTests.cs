using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using Microsoft.AspNetCore.Builder;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public sealed class TestModule { }

public class PersistedEventPublisherTests
{
    [Fact]
    public async Task PublishAsyncWritesTheSameEnvelopeToTheOutbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor<TestModule>>();
        var publisher = new PersistedEventPublisher<TestModule>(processor);
        var envelope = new MessageEnvelope(new SeatReservedIntegration());
        using var cancellation = new CancellationTokenSource();

        await publisher.PublishAsync(envelope, cancellation.Token);

        await processor.Received(1).PublishMessageAsync(envelope, cancellation.Token);
        await processor.DidNotReceiveWithAnyArgs().AddInternalMessageAsync<IInternalCommand>(default!, default);
    }

    [Fact]
    public async Task SendInternalAsyncStoresTheCommandAsAnInternalMessage()
    {
        var processor = Substitute.For<IPersistMessageProcessor<TestModule>>();
        var publisher = new PersistedEventPublisher<TestModule>(processor);
        var command = new ConfirmSeat();
        using var cancellation = new CancellationTokenSource();

        await publisher.SendInternalAsync(command, cancellation.Token);

        await processor.Received(1).AddInternalMessageAsync<IInternalCommand>(command, cancellation.Token);
        await processor.DidNotReceiveWithAnyArgs().PublishMessageAsync<MessageEnvelope>(default!, default);
    }

    [Fact]
    public async Task PropagatesOutboxFailures()
    {
        var processor = Substitute.For<IPersistMessageProcessor<TestModule>>();
        processor
            .PublishMessageAsync(Arg.Any<MessageEnvelope>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("outbox unavailable"));
        var publisher = new PersistedEventPublisher<TestModule>(processor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => publisher.PublishAsync(new MessageEnvelope(new SeatReservedIntegration()))
        );
    }

    [Fact]
    public async Task DispatcherRoutesIntegrationEventsAndInternalCommandsThroughTheOutbox()
    {
        var processor = Substitute.For<IPersistMessageProcessor<TestModule>>();
        var headers = Substitute.For<IEventHeadersProvider>();
        headers.GetHeaders().Returns(_ => new Dictionary<string, object?> { ["CorrelationId"] = "c-1" });
        var dispatcher = new EventDispatcher(
            [new SeatMapper()],
            new PersistedEventPublisher<TestModule>(processor),
            headers
        );

        await dispatcher.SendAsync<IDomainEvent>([new SeatReserved()], typeof(IInternalCommand));

        await processor
            .Received(1)
            .PublishMessageAsync(
                Arg.Is<MessageEnvelope>(e =>
                    e.Message is SeatReservedIntegration && (string)e.Headers["CorrelationId"]! == "c-1"
                ),
                Arg.Any<CancellationToken>()
            );
        await processor
            .Received(1)
            .AddInternalMessageAsync<IInternalCommand>(Arg.Any<ConfirmSeat>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddPersistMessageProcessorRegistersOnlyModuleScopedDispatch()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddPersistMessageProcessor<TestModule>("Test");

        var descriptor = Assert.Single(builder.Services, d => d.ServiceType == typeof(IEventDispatcher<TestModule>));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IEventPublisher));
        Assert.DoesNotContain(builder.Services, d => d.ServiceType == typeof(IPersistMessageProcessor));
        Assert.Equal(Microsoft.Extensions.DependencyInjection.ServiceLifetime.Scoped, descriptor.Lifetime);
    }
}
