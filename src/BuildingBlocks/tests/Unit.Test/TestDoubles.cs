using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Unit.Test;

public sealed record FlightCreatedDomainEvent(Guid Id) : IDomainEvent;

public sealed record BookingCreatedDomainEvent(Guid Id) : IDomainEvent;

public sealed record FlightCreatedIntegrationEvent(Guid Id) : IIntegrationEvent;

public sealed record BookingCreatedIntegrationEvent(Guid Id) : IIntegrationEvent;

public sealed record CreateFlightReadModel(Guid Id) : IInternalCommand;

public sealed class FlightMapper : IEventMapper
{
    public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) =>
        @event is FlightCreatedDomainEvent e ? new FlightCreatedIntegrationEvent(e.Id) : null;

    public IInternalCommand? MapToInternalCommand(IDomainEvent @event) =>
        @event is FlightCreatedDomainEvent e ? new CreateFlightReadModel(e.Id) : null;
}

public sealed class BookingMapper : IEventMapper
{
    public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event) =>
        @event is BookingCreatedDomainEvent e ? new BookingCreatedIntegrationEvent(e.Id) : null;

    public IInternalCommand? MapToInternalCommand(IDomainEvent @event) => null;
}

public sealed class RecordingPublisher : IIntegrationEventPublisher
{
    public List<MessageEnvelope> Published { get; } = [];

    public List<IInternalCommand> InternalCommands { get; } = [];

    public Task PublishAsync(MessageEnvelope messageEnvelope, CancellationToken cancellationToken = default)
    {
        Published.Add(messageEnvelope);
        return Task.CompletedTask;
    }

    public Task AddInternalMessageAsync<TCommand>(
        TCommand internalCommand,
        CancellationToken cancellationToken = default
    )
        where TCommand : class, IInternalCommand
    {
        InternalCommands.Add(internalCommand);
        return Task.CompletedTask;
    }
}
