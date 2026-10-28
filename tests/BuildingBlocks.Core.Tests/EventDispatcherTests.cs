using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Core.Tests;

public class EventDispatcherTests
{
    [Fact]
    public async Task RegistersOnlySelectedMappersWithoutWebOrPersistence()
    {
        var publisher = new RecordingPublisher();
        var services = new ServiceCollection();
        services.AddSingleton<IEventPublisher>(publisher);
        services.AddEventDispatcher();
        services.AddEventMapper<BookingMapper>();
        services.AddEventMapper<BookingMapper>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.Single(scope.ServiceProvider.GetServices<IEventMapper>());
        var dispatcher = scope.ServiceProvider.GetRequiredService<IEventDispatcher>();
        await dispatcher.SendAsync<IDomainEvent>(new Booked());
        await dispatcher.SendAsync<IDomainEvent>(new Unmapped());

        var message = Assert.Single(publisher.Messages);
        Assert.IsType<BookingCreated>(message.Message);
        Assert.Empty(message.Headers);
    }

    [Fact]
    public async Task ComposesMultipleMappersAndForwardsHeadersAndCancellation()
    {
        var publisher = new RecordingPublisher();
        var headers = new HeadersProvider();
        var dispatcher = new EventDispatcher([new EmptyMapper(), new BookingMapper()], publisher, headers);
        using var cancellation = new CancellationTokenSource();

        await dispatcher.SendAsync<IEvent>([new Booked(), new BookingCreated()], cancellationToken: cancellation.Token);

        Assert.Equal(2, publisher.Messages.Count);
        Assert.All(publisher.Messages, message => Assert.Equal("correlation", message.Headers["CorrelationId"]));
        Assert.Equal(cancellation.Token, publisher.LastToken);
    }

    [Fact]
    public async Task PublishesMarkedDomainEventsWithoutSuppressingMappedEventsInSameBatch()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new EventDispatcher([new BookingMapper()], publisher, new HeadersProvider());
        var wrapped = new Wrapped();

        await dispatcher.SendAsync<IDomainEvent>([wrapped, new Booked(), new Unmapped()]);

        Assert.Equal(2, publisher.Messages.Count);
        Assert.Same(
            wrapped,
            Assert.IsType<IntegrationEventWrapper<Wrapped>>(publisher.Messages[0].Message).DomainEvent
        );
        Assert.IsType<BookingCreated>(publisher.Messages[1].Message);
    }

    [Fact]
    public async Task SendsMappedInternalCommandsOnlyWhenRequested()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new EventDispatcher([new BookingMapper()], publisher, new HeadersProvider());
        using var cancellation = new CancellationTokenSource();

        await dispatcher.SendAsync<IDomainEvent>(new Booked());
        Assert.Empty(publisher.Commands);
        await dispatcher.SendAsync<IDomainEvent>(
            [new Booked(), new Unmapped()],
            typeof(IInternalCommand),
            cancellation.Token
        );

        Assert.IsType<ConfirmBooking>(Assert.Single(publisher.Commands));
        Assert.Equal(cancellation.Token, publisher.LastToken);
    }

    [Fact]
    public async Task AllowsNoMappersAndEmptyBatches()
    {
        var publisher = new RecordingPublisher();
        var dispatcher = new EventDispatcher([], publisher, new HeadersProvider());

        await dispatcher.SendAsync<IDomainEvent>([]);
        await dispatcher.SendAsync<IDomainEvent>(new Unmapped());
        await dispatcher.SendAsync<IIntegrationEvent>(new BookingCreated());

        Assert.Single(publisher.Messages);
    }

    [Fact]
    public async Task PropagatesPublisherFailure()
    {
        var dispatcher = new EventDispatcher([], new FailingPublisher(), new HeadersProvider());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SendAsync<IIntegrationEvent>(new BookingCreated())
        );
    }

    [Fact]
    public void CoreDoesNotReferenceInfrastructureAssemblies()
    {
        var dependencies = typeof(EventDispatcher).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.DoesNotContain(dependencies, name => name!.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(dependencies, name => name!.StartsWith("Microsoft.AspNetCore"));
        Assert.DoesNotContain(dependencies, name => name!.StartsWith("BuildingBlocks.PersistMessageProcessor"));
    }

    public record Booked : IDomainEvent;

    public record Unmapped : IDomainEvent;

    public record Wrapped : IDomainEvent, IHaveIntegrationEvent;

    public record BookingCreated : IIntegrationEvent;

    public record ConfirmBooking : IInternalCommand;

    public class EmptyMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) => null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
    }

    public class BookingMapper : IEventMapper
    {
        public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) =>
            @event is Booked ? new BookingCreated() : null;

        public IInternalCommand? MapToInternalCommand(IDomainEvent @event) =>
            @event is Booked ? new ConfirmBooking() : null;
    }

    private class HeadersProvider : IEventHeadersProvider
    {
        public IDictionary<string, object?> GetHeaders() =>
            new Dictionary<string, object?> { ["CorrelationId"] = "correlation" };
    }

    private class RecordingPublisher : IEventPublisher
    {
        public List<MessageEnvelope> Messages { get; } = [];
        public List<IInternalCommand> Commands { get; } = [];
        public CancellationToken LastToken { get; private set; }

        public Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }

        public Task SendInternalAsync(IInternalCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            LastToken = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private class FailingPublisher : IEventPublisher
    {
        public Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("publisher unavailable");

        public Task SendInternalAsync(IInternalCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("publisher unavailable");
    }
}
