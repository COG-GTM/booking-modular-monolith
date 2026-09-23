using AutoBogus;

namespace Unit.Test.Fakes;

using System;
using System.Linq;
using global::Flight.Data.Seed;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using MassTransit;

public sealed class FakeUpdateFlightCommand : AutoFaker<UpdateFlight>
{
    public FakeUpdateFlightCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.FlightNumber, r => r.Random.Number(1000, 2000).ToString());
        RuleFor(r => r.DepartureAirportId, _ => InitialData.Airports.First().Id);
        RuleFor(r => r.ArriveAirportId, _ => InitialData.Airports.Last().Id);
        RuleFor(r => r.AircraftId, _ => InitialData.Aircrafts.First().Id);
        RuleFor(r => r.Status, _ => FlightStatus.Flying);
        RuleFor(r => r.Price, r => r.Random.Decimal(1, 1000));
        RuleFor(r => r.DurationMinutes, r => r.Random.Decimal(1, 600));
        RuleFor(r => r.FlightDate, _ => DateTime.UtcNow.AddDays(1));
    }
}
