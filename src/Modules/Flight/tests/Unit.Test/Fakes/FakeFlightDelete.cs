namespace Unit.Test.Fakes;

using global::Flight.Flights.Models;

public static class FakeFlightDelete
{
    public static void Generate(Flight flight)
    {
        flight.Delete(
            flight.Id,
            flight.FlightNumber,
            flight.AircraftId,
            flight.DepartureAirportId,
            flight.DepartureDate,
            flight.ArriveDate,
            flight.ArriveAirportId,
            flight.DurationMinutes,
            flight.FlightDate,
            flight.Status,
            flight.Price
        );
    }
}
