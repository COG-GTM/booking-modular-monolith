namespace Unit.Test.Identity;

using global::Identity;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Core.Model;
using FluentAssertions;
using Xunit;

public class IdentityEventMapperTests
{
    private readonly IdentityEventMapper _mapper = new();

    [Fact]
    public void map_to_integration_event_should_return_null_for_any_domain_event()
    {
        _mapper.MapToIntegrationEvent(new TestDomainEvent()).Should().BeNull();
    }

    [Fact]
    public void map_to_internal_command_should_return_null_for_any_domain_event()
    {
        _mapper.MapToInternalCommand(new TestDomainEvent()).Should().BeNull();
    }

    private sealed record TestDomainEvent : IDomainEvent
    {
        public Guid EventId { get; } = Guid.NewGuid();
        public DateTime OccurredOn { get; } = DateTime.UtcNow;
        public string EventType { get; } = nameof(TestDomainEvent);
    }
}
