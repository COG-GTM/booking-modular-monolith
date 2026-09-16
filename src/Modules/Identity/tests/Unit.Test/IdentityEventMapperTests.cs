using BuildingBlocks.Core.Event;
using FluentAssertions;
using Identity;
using NSubstitute;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test;

[Collection(nameof(UnitTestFixture))]
public class IdentityEventMapperTests
{
    private readonly IdentityEventMapper _mapper = new();

    [Fact]
    public void unknown_domain_event_should_not_map_to_integration_event()
    {
        _mapper.MapToIntegrationEvent(Substitute.For<IDomainEvent>()).Should().BeNull();
    }

    [Fact]
    public void unknown_domain_event_should_not_map_to_internal_command()
    {
        _mapper.MapToInternalCommand(Substitute.For<IDomainEvent>()).Should().BeNull();
    }
}
