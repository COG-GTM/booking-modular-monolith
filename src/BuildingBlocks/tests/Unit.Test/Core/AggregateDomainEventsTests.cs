using BuildingBlocks.Core.Event;
using BuildingBlocks.Core.Model;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Unit.Test.CoreTests;

public class AggregateDomainEventsTests
{
    private sealed record CoreTestAggregate : Aggregate<Guid>;

    private sealed record CoreAggregateDomainEvent(Guid Id) : IDomainEvent;

    [Fact]
    public void add_domain_event_appends_events_in_order()
    {
        var aggregate = new CoreTestAggregate();
        var first = new CoreAggregateDomainEvent(Guid.NewGuid());
        var second = new CoreAggregateDomainEvent(Guid.NewGuid());

        aggregate.AddDomainEvent(first);
        aggregate.AddDomainEvent(second);

        aggregate.DomainEvents.Should().Equal(first, second);
    }

    [Fact]
    public void clear_domain_events_returns_the_events_and_empties_the_collection()
    {
        var aggregate = new CoreTestAggregate();
        var first = new CoreAggregateDomainEvent(Guid.NewGuid());
        var second = new CoreAggregateDomainEvent(Guid.NewGuid());
        aggregate.AddDomainEvent(first);
        aggregate.AddDomainEvent(second);

        var cleared = aggregate.ClearDomainEvents();

        cleared.Should().Equal(first, second);
        aggregate.DomainEvents.Should().BeEmpty();
        aggregate.ClearDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void domain_events_is_read_only_so_mutating_it_cannot_change_the_aggregate()
    {
        var aggregate = new CoreTestAggregate();
        var domainEvent = new CoreAggregateDomainEvent(Guid.NewGuid());
        aggregate.AddDomainEvent(domainEvent);

        aggregate.DomainEvents.Should().BeOfType<System.Collections.ObjectModel.ReadOnlyCollection<IDomainEvent>>();
    }
}
