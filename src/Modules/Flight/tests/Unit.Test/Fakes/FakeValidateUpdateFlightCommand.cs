using System;
using AutoBogus;
using Flight.Flights.Enums;

namespace Unit.Test.Fakes;

using global::Flight.Flights.Features.UpdatingFlight.V1;

public class FakeValidateUpdateFlightCommand : AutoFaker<UpdateFlight>
{
    public FakeValidateUpdateFlightCommand()
    {
        RuleFor(r => r.Price, _ => -10);
        RuleFor(r => r.Status, _ => (FlightStatus)10);
        RuleFor(r => r.AircraftId, _ => Guid.Empty);
        RuleFor(r => r.DepartureAirportId, _ => Guid.Empty);
        RuleFor(r => r.ArriveAirportId, _ => Guid.Empty);
        RuleFor(r => r.DurationMinutes, _ => 0);
        RuleFor(r => r.FlightDate, _ => new DateTime());
    }
}
