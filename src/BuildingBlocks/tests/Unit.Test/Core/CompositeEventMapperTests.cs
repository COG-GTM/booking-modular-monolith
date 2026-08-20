using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Unit.Test.Core;

public class CompositeEventMapperTests
{
    [Fact]
    public void map_to_integration_event_should_return_first_non_null_result()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid());
        var expected = new FakeIntegrationEvent(domainEvent.Id);

        var nonMatchingMapper = Substitute.For<IEventMapper>();
        nonMatchingMapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);

        var matchingMapper = Substitute.For<IEventMapper>();
        matchingMapper.MapToIntegrationEvent(domainEvent).Returns(expected);

        var composite = new CompositeEventMapper(new[] { nonMatchingMapper, matchingMapper });

        composite.MapToIntegrationEvent(domainEvent).Should().Be(expected);
    }

    [Fact]
    public void map_to_integration_event_should_return_null_when_no_mapper_matches()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid());

        var mapper = Substitute.For<IEventMapper>();
        mapper.MapToIntegrationEvent(domainEvent).Returns((IIntegrationEvent?)null);

        var composite = new CompositeEventMapper(new[] { mapper });

        composite.MapToIntegrationEvent(domainEvent).Should().BeNull();
    }

    [Fact]
    public void map_to_internal_command_should_return_first_non_null_result()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid());
        var expected = new FakeInternalCommand(domainEvent.Id);

        var nonMatchingMapper = Substitute.For<IEventMapper>();
        nonMatchingMapper.MapToInternalCommand(domainEvent).Returns((IInternalCommand?)null);

        var matchingMapper = Substitute.For<IEventMapper>();
        matchingMapper.MapToInternalCommand(domainEvent).Returns(expected);

        var composite = new CompositeEventMapper(new[] { nonMatchingMapper, matchingMapper });

        composite.MapToInternalCommand(domainEvent).Should().Be(expected);
    }

    [Fact]
    public void map_to_internal_command_should_return_null_when_no_mapper_matches()
    {
        var domainEvent = new FakeDomainEvent(Guid.NewGuid());

        var mapper = Substitute.For<IEventMapper>();
        mapper.MapToInternalCommand(domainEvent).Returns((IInternalCommand?)null);

        var composite = new CompositeEventMapper(new[] { mapper });

        composite.MapToInternalCommand(domainEvent).Should().BeNull();
    }
}
