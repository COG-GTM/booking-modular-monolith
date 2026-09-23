namespace Integration.Test.Fakes;

using System;
using System.Linq;
using AutoBogus;
using global::Flight.Data.Seed;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.UpdatingFlight.V1;

public sealed class FakeUpdateFlightMongoCommand : AutoFaker<UpdateFlightMongo>
{
    public FakeUpdateFlightMongoCommand(Guid flightId)
    {
        RuleFor(r => r.Id, _ => flightId);
        RuleFor(r => r.FlightNumber, _ => "34UU");
        RuleFor(r => r.DepartureAirportId, _ => InitialData.Airports.Last().Id);
        RuleFor(r => r.ArriveAirportId, _ => InitialData.Airports.First().Id);
        RuleFor(r => r.AircraftId, _ => InitialData.Aircrafts.Last().Id);
        RuleFor(r => r.DepartureDate, _ => new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc));
        RuleFor(r => r.ArriveDate, _ => new DateTime(2030, 1, 1, 14, 30, 0, DateTimeKind.Utc));
        RuleFor(r => r.FlightDate, _ => new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        RuleFor(r => r.DurationMinutes, _ => 270);
        RuleFor(r => r.Status, _ => FlightStatus.Delay);
        RuleFor(r => r.Price, _ => 950);
        RuleFor(r => r.IsDeleted, _ => false);
    }
}
