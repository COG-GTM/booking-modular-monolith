using BuildingBlocks.Core.CQRS;
using BuildingBlocks.Core.Event;

namespace Unit.Test.Fakes;

public record FakeIntegrationEvent(Guid EventId, string Name) : IIntegrationEvent;

public record FakeDomainEvent(Guid EventId, string Name) : IDomainEvent;

public record FakeDomainEventWithIntegrationEvent(Guid EventId, string Name) : IDomainEvent, IHaveIntegrationEvent;

public record FakeInternalCommand(Guid EventId, string Name) : IInternalCommand, ICommand;

public record FakeNotAnEvent(string Name);
