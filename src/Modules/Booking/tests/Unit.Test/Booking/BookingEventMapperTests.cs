using Booking;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using MediatR;
using Xunit;

namespace Unit.Test;

public class BookingEventMapperTests
{
    private sealed record OtherEvent : IDomainEvent, INotification;

    [Fact]
    public void non_booking_domain_event_has_no_integration_event()
    {
        new BookingEventMapper().MapToIntegrationEvent(new OtherEvent()).Should().BeNull();
    }
}
