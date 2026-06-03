using BuildingBlocks.Core.Event;
using Identity;
using NSubstitute;
using Xunit;

namespace Unit.Test.Root;

public class IdentityEventMapperTests
{
    private readonly IdentityEventMapper _mapper = new();

    [Fact]
    public void map_to_integration_event_returns_null()
    {
        var domainEvent = Substitute.For<IDomainEvent>();
        var result = _mapper.MapToIntegrationEvent(domainEvent);
        Assert.Null(result);
    }

    [Fact]
    public void map_to_internal_command_returns_null()
    {
        var domainEvent = Substitute.For<IDomainEvent>();
        var result = _mapper.MapToInternalCommand(domainEvent);
        Assert.Null(result);
    }
}
