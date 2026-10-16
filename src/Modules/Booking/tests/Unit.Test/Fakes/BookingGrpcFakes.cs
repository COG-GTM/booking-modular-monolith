using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Google.Protobuf.WellKnownTypes;

namespace Unit.Test.Fakes;

internal static class BookingGrpcFakes
{
    public static readonly DateTime FlightDate = new(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc);
    public static readonly Guid FlightId = Guid.NewGuid();
    public static readonly Guid AircraftId = Guid.NewGuid();
    public static readonly Guid DepartureAirportId = Guid.NewGuid();
    public static readonly Guid ArriveAirportId = Guid.NewGuid();

    public static GetFlightByIdResult Flight(string flightNumber = "BA123") =>
        new()
        {
            FlightDto = new FlightResponse
            {
                Id = FlightId.ToString(),
                FlightNumber = flightNumber,
                AircraftId = AircraftId.ToString(),
                DepartureAirportId = DepartureAirportId.ToString(),
                ArriveAirportId = ArriveAirportId.ToString(),
                FlightDate = Timestamp.FromDateTime(FlightDate),
                Price = 199.99,
            },
        };

    public static GetPassengerByIdResult Passenger(string name = "Jane Doe") =>
        new()
        {
            PassengerDto = new PassengerResponse
            {
                Id = Guid.NewGuid().ToString(),
                Name = name,
                PassportNumber = "P123456",
            },
        };

    public static GetAvailableSeatsResult AvailableSeats(params string[] seatNumbers)
    {
        var result = new GetAvailableSeatsResult();
        result.SeatDtos.AddRange(
            seatNumbers.Select(seatNumber => new SeatDtoResponse
            {
                FlightId = FlightId.ToString(),
                SeatNumber = seatNumber,
            })
        );
        return result;
    }
}
