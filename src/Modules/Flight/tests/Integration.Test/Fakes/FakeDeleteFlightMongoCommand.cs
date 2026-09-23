namespace Integration.Test.Fakes;

using System;
using System.Linq;
using AutoBogus;
using global::Flight.Data.Seed;
using global::Flight.Flights.Enums;
using global::Flight.Flights.Features.DeletingFlight.V1;

public sealed class FakeDeleteFlightMongoCommand : AutoFaker<DeleteFlightMongo>
{
    public FakeDeleteFlightMongoCommand(Guid flightId)
    {
        RuleFor(r => r.Id, _ => flightId);
        RuleFor(r => r.FlightNumber, _ => "12FF");
        RuleFor(r => r.DepartureAirportId, _ => InitialData.Airports.First().Id);
        RuleFor(r => r.ArriveAirportId, _ => InitialData.Airports.Last().Id);
        RuleFor(r => r.AircraftId, _ => InitialData.Aircrafts.First().Id);
        RuleFor(r => r.Status, _ => FlightStatus.Flying);
        RuleFor(r => r.IsDeleted, _ => true);
    }
}
