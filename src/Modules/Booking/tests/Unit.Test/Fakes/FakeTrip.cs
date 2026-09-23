namespace Unit.Test.Fakes;

using global::Booking.Booking.ValueObjects;
using MassTransit;

public static class FakeTrip
{
    public const string FlightNumber = "BD467";
    public static readonly Guid AircraftId = NewId.NextGuid();
    public static readonly Guid DepartureAirportId = NewId.NextGuid();
    public static readonly Guid ArriveAirportId = NewId.NextGuid();
    public static readonly DateTime FlightDate = new(2030, 5, 20, 8, 30, 0, DateTimeKind.Utc);
    public const decimal Price = 850m;
    public const string Description = "Window seat, economy";
    public const string SeatNumber = "12A";

    public static Trip Generate()
    {
        return Trip.Of(
            FlightNumber,
            AircraftId,
            DepartureAirportId,
            ArriveAirportId,
            FlightDate,
            Price,
            Description,
            SeatNumber
        );
    }
}
