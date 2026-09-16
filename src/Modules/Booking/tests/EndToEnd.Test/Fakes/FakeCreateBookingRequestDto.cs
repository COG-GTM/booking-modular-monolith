using AutoBogus;

namespace EndToEnd.Test.Fakes;

using System;
using global::Booking.Booking.Features.CreatingBook.V1;

public sealed class FakeCreateBookingRequestDto : AutoFaker<CreateBookingRequestDto>
{
    public FakeCreateBookingRequestDto()
    {
        RuleFor(r => r.FlightId, _ => new Guid("3c5c0000-97c6-fc34-2eb9-08db322230c9"));
        RuleFor(r => r.PassengerId, _ => new Guid("4c5c8888-97c6-fc34-2eb9-18db322230c1"));
        RuleFor(r => r.Description, _ => "End to end booking");
    }
}
