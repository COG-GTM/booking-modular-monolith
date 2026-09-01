// Integration contracts are versioned and treated as immutable once published.
// To evolve a contract: create a new record in a V2 namespace.
// Never modify existing published records.
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Contracts.EventBus.Messages.V1;

public record PassengerRegistrationCompleted(Guid Id) : IIntegrationEvent;

public record PassengerCreated(Guid Id) : IIntegrationEvent;
