namespace Unit.Test.Fakes;

using System;
using global::Booking.Booking.ValueObjects;
using MassTransit;

public static class FakeBookingCreate
{
    public static global::Booking.Booking.Models.Booking Generate()
    {
        return global::Booking.Booking.Models.Booking.Create(
            NewId.NextGuid(),
            PassengerInfo.Of("Sam"),
            FakeTrip.Generate(),
            false,
            1
        );
    }
}

public static class FakeTrip
{
    public static Trip Generate()
    {
        return Trip.Of(
            "1500B",
            new Guid("3c5c0000-97c6-fc34-fcd3-08db322230c8"),
            new Guid("3c5c0000-97c6-fc34-fc3c-08db322230c8"),
            new Guid("3c5c0000-97c6-fc34-a0cb-08db322230c8"),
            new DateTime(2024, 1, 31, 12, 0, 0),
            100m,
            "Business trip",
            "33F"
        );
    }
}
