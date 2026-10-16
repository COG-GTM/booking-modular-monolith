using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.CoreTests;

public class CompositeEventMapperTests
{
    private sealed record CoreCompositeDomainEvent(Guid Id) : IDomainEvent;

    private sealed record CoreCompositeIntegrationEvent(Guid Id) : IIntegrationEvent;

    private sealed record CoreCompositeInternalCommand(Guid Id) : IInternalCommand;

    [Fact]
    public void first_non_null_integration_event_mapper_wins_and_later_mappers_are_not_called()
    {
        var domainEvent = new CoreCompositeDomainEvent(Guid.NewGuid());
        var expected = new CoreCompositeIntegrationEvent(domainEvent.Id);
        var first = Substitute.For<IEventMapper>();
        first.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        var second = Substitute.For<IEventMapper>();
        second.MapToIntegrationEvent(domainEvent).Returns(expected);
        var third = Substitute.For<IEventMapper>();
        var sut = new CompositeEventMapper([first, second, third]);

        var result = sut.MapToIntegrationEvent(domainEvent);

        result.Should().BeSameAs(expected);
        third.DidNotReceive().MapToIntegrationEvent(Arg.Any<IDomainEvent>());
    }

    [Fact]
    public void first_non_null_internal_command_mapper_wins_and_later_mappers_are_not_called()
    {
        var domainEvent = new CoreCompositeDomainEvent(Guid.NewGuid());
        var expected = new CoreCompositeInternalCommand(domainEvent.Id);
        var first = Substitute.For<IEventMapper>();
        first.MapToInternalCommand(domainEvent).Returns((IInternalCommand?)null);
        var second = Substitute.For<IEventMapper>();
        second.MapToInternalCommand(domainEvent).Returns(expected);
        var third = Substitute.For<IEventMapper>();
        var sut = new CompositeEventMapper([first, second, third]);

        var result = sut.MapToInternalCommand(domainEvent);

        result.Should().BeSameAs(expected);
        third.DidNotReceive().MapToInternalCommand(Arg.Any<IDomainEvent>());
    }

    [Fact]
    public void all_null_results_yield_null_for_both_mapping_directions()
    {
        var domainEvent = new CoreCompositeDomainEvent(Guid.NewGuid());
        var first = Substitute.For<IEventMapper>();
        first.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        first.MapToInternalCommand(domainEvent).Returns((IInternalCommand?)null);
        var second = Substitute.For<IEventMapper>();
        second.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);
        second.MapToInternalCommand(domainEvent).Returns((IInternalCommand?)null);
        var sut = new CompositeEventMapper([first, second]);

        sut.MapToIntegrationEvent(domainEvent).Should().BeNull();
        sut.MapToInternalCommand(domainEvent).Should().BeNull();
    }

    [Fact]
    public void empty_mapper_list_yields_null()
    {
        var sut = new CompositeEventMapper(Array.Empty<IEventMapper>());
        var domainEvent = new CoreCompositeDomainEvent(Guid.NewGuid());

        sut.MapToIntegrationEvent(domainEvent).Should().BeNull();
        sut.MapToInternalCommand(domainEvent).Should().BeNull();
    }
}
