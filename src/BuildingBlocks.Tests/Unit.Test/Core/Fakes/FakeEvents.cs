using BuildingBlocks.Core.Event;

namespace Unit.Test.Core.Fakes;

public record FakeDomainEvent : IDomainEvent;

public record FakeIntegrationEvent : IIntegrationEvent;

public record FakeInternalCommand : IInternalCommand;
