using Flight.Aircrafts.ValueObjects;
using Flight.Airports.ValueObjects;
using Flight.Data;
using Flight.Flights.Enums;
using Flight.Flights.ValueObjects;
using Identity.Data;
using Identity.Identity.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Passenger.Data;
using Passenger.Passengers.ValueObjects;
using Unit.Test.Common;
using Name = Passenger.Passengers.ValueObjects.Name;

namespace Unit.Test.Modules;

public static class ModuleFakes
{
    public static FlightDbContext CreateFlightDbContext(string? databaseName = null)
    {
        return new FlightDbContext(
            DbContextFactory.Options<FlightDbContext>(databaseName ?? DbContextFactory.NewDatabaseName()),
            null,
            Substitute.For<ILogger<FlightDbContext>>()
        );
    }

    public static PassengerDbContext CreatePassengerDbContext(string? databaseName = null)
    {
        return new PassengerDbContext(
            DbContextFactory.Options<PassengerDbContext>(databaseName ?? DbContextFactory.NewDatabaseName()),
            null,
            Substitute.For<ILogger<PassengerDbContext>>()
        );
    }

    public static IdentityContext CreateIdentityContext(string? databaseName = null)
    {
        return new IdentityContext(
            DbContextFactory.Options<IdentityContext>(databaseName ?? DbContextFactory.NewDatabaseName()),
            Substitute.For<ILogger<IdentityContext>>()
        );
    }

    public static Flight.Flights.Models.Flight CreateFlight()
    {
        return Flight.Flights.Models.Flight.Create(
            FlightId.Of(NewId.NextGuid()),
            FlightNumber.Of("BB100"),
            AircraftId.Of(NewId.NextGuid()),
            AirportId.Of(NewId.NextGuid()),
            DepartureDate.Of(new DateTime(2030, 1, 1, 10, 0, 0)),
            ArriveDate.Of(new DateTime(2030, 1, 1, 12, 0, 0)),
            AirportId.Of(NewId.NextGuid()),
            DurationMinutes.Of(120m),
            FlightDate.Of(new DateTime(2030, 1, 1, 11, 0, 0)),
            FlightStatus.Flying,
            Price.Of(100m)
        );
    }

    public static Passenger.Passengers.Models.Passenger CreatePassenger()
    {
        return Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(NewId.NextGuid()),
            Name.Of("Jane Doe"),
            PassportNumber.Of("P1234567")
        );
    }

    public static User CreateUser(string userName = "jane")
    {
        return new User
        {
            Id = NewId.NextGuid(),
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            FirstName = "Jane",
            LastName = "Doe",
            PassPortNumber = "P1234567",
            SecurityStamp = Guid.NewGuid().ToString(),
        };
    }
}
