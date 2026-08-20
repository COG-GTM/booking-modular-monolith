using BuildingBlocks.Core.Event;

namespace Unit.Test.Core;

public record FakeDomainEvent(Guid Id) : IDomainEvent;

public record FakeWrappedDomainEvent(Guid Id) : IDomainEvent, IHaveIntegrationEvent;

public record FakeIntegrationEvent(Guid Id) : IIntegrationEvent;

public record FakeInternalCommand(Guid Id) : IInternalCommand;
