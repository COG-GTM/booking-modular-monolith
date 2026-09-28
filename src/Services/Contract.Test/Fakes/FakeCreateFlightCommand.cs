namespace Contract.Test.Fakes;

using System.Linq;
using AutoBogus;
using Flight.Data.Seed;
using Flight.Flights.Enums;
using Flight.Flights.Features.CreatingFlight.V1;
using MassTransit;

public sealed class FakeCreateFlightCommand : AutoFaker<CreateFlight>
{
    public FakeCreateFlightCommand()
    {
        RuleFor(r => r.Id, _ => NewId.NextGuid());
        RuleFor(r => r.FlightNumber, r => r.Random.Number(1000, 2000).ToString());
        RuleFor(r => r.DepartureAirportId, _ => InitialData.Airports.First().Id);
        RuleFor(r => r.ArriveAirportId, _ => InitialData.Airports.Last().Id);
        RuleFor(r => r.Status, _ => FlightStatus.Flying);
        RuleFor(r => r.AircraftId, _ => InitialData.Aircrafts.First().Id);
    }
}
