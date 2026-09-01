using System.Reflection;
using BuildingBlocks.Mongo;
using Flight.Data;
using Flight.Flights.Models;
using Flight.Seats.Models;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NSubstitute;

namespace Unit.Test.Common;

public static class MockFlightReadDbContext
{
    public static FlightReadDbContext Create(
        IMongoCollection<FlightReadModel>? flightCollection = null,
        IMongoCollection<SeatReadModel>? seatCollection = null)
    {
        var options = Options.Create(new MongoOptions
        {
            ConnectionString = "mongodb://localhost:27017",
            DatabaseName = "test_flight_db"
        });

        var context = new FlightReadDbContext(options);

        if (flightCollection != null)
        {
            var flightField = typeof(FlightReadDbContext).GetProperty(nameof(FlightReadDbContext.Flight))!
                .GetBackingField();
            flightField?.SetValue(context, flightCollection);
        }

        if (seatCollection != null)
        {
            var seatField = typeof(FlightReadDbContext).GetProperty(nameof(FlightReadDbContext.Seat))!
                .GetBackingField();
            seatField?.SetValue(context, seatCollection);
        }

        return context;
    }

    private static FieldInfo? GetBackingField(this PropertyInfo property)
    {
        var backingFieldName = $"<{property.Name}>k__BackingField";
        return property.DeclaringType?.GetField(backingFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
    }
}
