using AutoBogus;

namespace EndToEnd.Test.Fakes;

using global::Flight.Flights.Features.UpdatingFlight.V1;

public sealed class FakeUpdateFlightCommand : AutoFaker<UpdateFlight>
{
    public FakeUpdateFlightCommand(global::Flight.Flights.Models.Flight flight)
    {
        RuleFor(r => r.Id, _ => flight.Id.Value);
        RuleFor(r => r.FlightNumber, r => r.Random.Number(1000, 2000).ToString());
        RuleFor(r => r.AircraftId, _ => flight.AircraftId.Value);
        RuleFor(r => r.DepartureAirportId, _ => flight.DepartureAirportId.Value);
        RuleFor(r => r.DepartureDate, _ => flight.DepartureDate.Value);
        RuleFor(r => r.ArriveDate, _ => flight.ArriveDate.Value);
        RuleFor(r => r.ArriveAirportId, _ => flight.ArriveAirportId.Value);
        RuleFor(r => r.DurationMinutes, _ => flight.DurationMinutes.Value);
        RuleFor(r => r.FlightDate, _ => flight.FlightDate.Value);
        RuleFor(r => r.Status, _ => flight.Status);
        RuleFor(r => r.IsDeleted, _ => false);
        RuleFor(r => r.Price, _ => 800);
    }
}
