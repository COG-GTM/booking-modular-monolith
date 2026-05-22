using BuildingBlocks.Core.Event;

namespace SharedContracts.EventBus.Messages;

public record PassengerRegistrationCompleted(Guid Id) : IIntegrationEvent;

public record PassengerCreated(Guid Id) : IIntegrationEvent;
