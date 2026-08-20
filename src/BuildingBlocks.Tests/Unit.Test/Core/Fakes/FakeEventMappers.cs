using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;

namespace Unit.Test.Core.Fakes;

public class FakeMatchingEventMapper : IEventMapper
{
    public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event)
    {
        return new FakeIntegrationEvent();
    }

    public IInternalCommand? MapToInternalCommand(IDomainEvent @event)
    {
        return new FakeInternalCommand();
    }
}

public class FakeNonMatchingEventMapper : IEventMapper
{
    public IIntegrationEvent? MapToIntegrationEvent(IDomainEvent @event)
    {
        return null;
    }

    public IInternalCommand? MapToInternalCommand(IDomainEvent @event)
    {
        return null;
    }
}
