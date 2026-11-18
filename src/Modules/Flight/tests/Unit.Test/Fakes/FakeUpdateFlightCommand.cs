using System;
using AutoBogus;
using Flight.Flights.Enums;
using MassTransit;

namespace Unit.Test.Fakes;

using global::Flight.Flights.Features.UpdatingFlight.V1;

public class FakeUpdateFlightCommand : AutoFaker<UpdateFlight>
{
    public FakeUpdateFlightCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.FlightNumber, r => r.Random.Number(1000, 2000).ToString());
        RuleFor(r => r.AircraftId, _ => NewId.NextGuid());
        RuleFor(r => r.DepartureAirportId, _ => NewId.NextGuid());
        RuleFor(r => r.ArriveAirportId, _ => NewId.NextGuid());
        RuleFor(r => r.Status, _ => FlightStatus.Flying);
        RuleFor(r => r.Price, r => r.Random.Decimal(1, 1000));
        RuleFor(r => r.DurationMinutes, r => r.Random.Decimal(1, 600));
        RuleFor(r => r.FlightDate, _ => DateTime.UtcNow.Date.AddDays(1));
    }
}
