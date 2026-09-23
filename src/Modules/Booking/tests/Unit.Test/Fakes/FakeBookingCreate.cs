namespace Unit.Test.Fakes;

using global::Booking.Booking.ValueObjects;
using MassTransit;

public static class FakeBookingCreate
{
    public const string PassengerName = "Jane Doe";

    public static global::Booking.Booking.Models.Booking Generate(
        Guid? id = null,
        bool isDeleted = false,
        long? userId = null
    )
    {
        return global::Booking.Booking.Models.Booking.Create(
            id ?? NewId.NextGuid(),
            PassengerInfo.Of(PassengerName),
            FakeTrip.Generate(),
            isDeleted,
            userId
        );
    }
}
