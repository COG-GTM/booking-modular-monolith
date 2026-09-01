// Integration contracts are versioned and treated as immutable once published.
// To evolve a contract: create a new record in a V2 namespace.
// Never modify existing published records.
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.Contracts.EventBus.Messages.V1;

public record FlightCreated(Guid Id) : IIntegrationEvent;

public record FlightUpdated(Guid Id) : IIntegrationEvent;

public record FlightDeleted(Guid Id) : IIntegrationEvent;

public record AircraftCreated(Guid Id) : IIntegrationEvent;

public record AirportCreated(Guid Id) : IIntegrationEvent;

public record SeatCreated(Guid Id) : IIntegrationEvent;

public record SeatReserved(Guid Id) : IIntegrationEvent;
