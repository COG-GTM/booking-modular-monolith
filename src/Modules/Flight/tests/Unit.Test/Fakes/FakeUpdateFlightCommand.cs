using AutoBogus;

namespace Unit.Test.Fakes;

using System.Linq;
using global::Flight.Data.Seed;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using MassTransit;

public sealed class FakeUpdateFlightCommand : AutoFaker<UpdateFlight>
{
    public FakeUpdateFlightCommand()
    {
        RuleFor(r => r.Id, _ => (Guid)InitialData.Flights.First().Id);
        RuleFor(r => r.FlightNumber, r => r.Random.Number(1000, 2000).ToString());
        RuleFor(r => r.DepartureAirportId, _ => InitialData.Airports.First().Id);
        RuleFor(r => r.ArriveAirportId, _ => InitialData.Airports.Last().Id);
        RuleFor(r => r.AircraftId, _ => InitialData.Aircrafts.First().Id);
    }
}
