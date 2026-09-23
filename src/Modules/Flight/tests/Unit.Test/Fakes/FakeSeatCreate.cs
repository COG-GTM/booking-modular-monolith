namespace Unit.Test.Fakes;

using global::Flight.Flights.ValueObjects;
using global::Flight.Seats.ValueObjects;

public static class FakeSeatCreate
{
    public static global::Flight.Seats.Models.Seat Generate()
    {
        var command = new FakeCreateSeatCommand().Generate();

        return global::Flight.Seats.Models.Seat.Create(
            SeatId.Of(command.Id),
            SeatNumber.Of(command.SeatNumber),
            command.Type,
            command.Class,
            FlightId.Of(command.FlightId)
        );
    }
}
