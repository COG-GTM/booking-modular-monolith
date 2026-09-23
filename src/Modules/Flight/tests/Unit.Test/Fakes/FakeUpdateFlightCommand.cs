using AutoBogus;

namespace Unit.Test.Fakes;

using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.UpdatingFlight.V1;

public sealed class FakeUpdateFlightCommand : AutoFaker<UpdateFlight>
{
    public FakeUpdateFlightCommand(global::Flight.Flights.Models.Flight flight)
    {
        RuleFor(r => r.Id, _ => flight.Id);
        RuleFor(r => r.DepartureAirportId, _ => flight.DepartureAirportId);
        RuleFor(r => r.ArriveAirportId, _ => flight.ArriveAirportId);
        RuleFor(r => r.AircraftId, _ => flight.AircraftId);
        RuleFor(r => r.FlightNumber, r => r.Random.Number(1000, 2000).ToString());
        RuleFor(r => r.DepartureDate, r => r.Date.Future());
        RuleFor(r => r.ArriveDate, (r, c) => c.DepartureDate.AddHours(2));
        RuleFor(r => r.FlightDate, (_, c) => c.DepartureDate);
        RuleFor(r => r.DurationMinutes, _ => 120);
        RuleFor(r => r.Status, _ => FlightStatus.Delay);
        RuleFor(r => r.Price, _ => 800);
        RuleFor(r => r.IsDeleted, _ => false);
    }
}
