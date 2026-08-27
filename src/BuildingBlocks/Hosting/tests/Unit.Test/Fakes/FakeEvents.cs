using BuildingBlocks.Core.Event;

namespace Unit.Test.Fakes;

public record FakeDomainEvent : IDomainEvent;

public record FakeUnmappedDomainEvent : IDomainEvent;

public record FakeIntegrationEvent : IIntegrationEvent;

public record FakeInternalCommand : IInternalCommand;
