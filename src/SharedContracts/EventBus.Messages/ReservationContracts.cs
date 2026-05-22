using BuildingBlocks.Core.Event;

namespace SharedContracts.EventBus.Messages;

public record BookingCreated(Guid Id) : IIntegrationEvent;
