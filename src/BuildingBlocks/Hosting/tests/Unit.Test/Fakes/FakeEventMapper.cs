using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;

namespace Unit.Test.Fakes;

public class FakeEventMapper : IEventMapper
{
    public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event)
    {
        return @event is FakeDomainEvent ? new FakeIntegrationEvent() : null;
    }

    public IInternalCommand? MapToInternalCommand(IDomainEvent @event)
    {
        return @event is FakeDomainEvent ? new FakeInternalCommand() : null;
    }
}
