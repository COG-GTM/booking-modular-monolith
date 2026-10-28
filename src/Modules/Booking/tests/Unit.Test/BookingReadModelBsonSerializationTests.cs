using Booking.Booking.Models;
using Booking.Booking.ValueObjects;
using Booking.Data;
using BuildingBlocks.Mongo;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using Xunit;

namespace Booking.Unit.Test;

public class BookingReadModelBsonSerializationTests
{
    [Fact]
    public void BookingReadModelSerializesAndDeserializesItsNestedValues()
    {
        using var context = new BookingReadDbContext(
            new TestOptionsSnapshot<MongoOptions>(
                new MongoOptions { ConnectionString = "mongodb://localhost", DatabaseName = "booking_test" }
            )
        );
        var readModel = CreateReadModel();

        var document = readModel.ToBsonDocument();
        var tripDocument = document["trip"].AsBsonDocument;
        var passengerDocument = document["passengerInfo"].AsBsonDocument;

        Assert.Equal(readModel.Id.ToString(), document["_id"].AsString);
        Assert.Equal(readModel.BookId.ToString(), document["bookId"].AsString);
        Assert.Equal("AB123", tripDocument["flightNumber"].AsString);
        Assert.Equal(readModel.Trip.AircraftId.ToString(), tripDocument["aircraftId"].AsString);
        Assert.Equal(readModel.Trip.DepartureAirportId.ToString(), tripDocument["departureAirportId"].AsString);
        Assert.Equal(readModel.Trip.ArriveAirportId.ToString(), tripDocument["arriveAirportId"].AsString);
        Assert.Equal(readModel.Trip.FlightDate, tripDocument["flightDate"].ToUniversalTime());
        Assert.Equal(readModel.Trip.Price, tripDocument["price"].AsDecimal);
        Assert.Equal("Window seat", tripDocument["description"].AsString);
        Assert.Equal("12A", tripDocument["seatNumber"].AsString);
        Assert.Equal("Test passenger", passengerDocument["name"].AsString);
        Assert.False(document["isDeleted"].AsBoolean);

        var deserialized = BsonSerializer.Deserialize<BookingReadModel>(document);

        Assert.Equal(readModel.Id, deserialized.Id);
        Assert.Equal(readModel.BookId, deserialized.BookId);
        Assert.Equal(readModel.IsDeleted, deserialized.IsDeleted);
        Assert.Equal(readModel.PassengerInfo.Name, deserialized.PassengerInfo.Name);
        Assert.Equal(readModel.Trip.FlightNumber, deserialized.Trip.FlightNumber);
        Assert.Equal(readModel.Trip.AircraftId, deserialized.Trip.AircraftId);
        Assert.Equal(readModel.Trip.DepartureAirportId, deserialized.Trip.DepartureAirportId);
        Assert.Equal(readModel.Trip.ArriveAirportId, deserialized.Trip.ArriveAirportId);
        Assert.Equal(readModel.Trip.FlightDate, deserialized.Trip.FlightDate);
        Assert.Equal(readModel.Trip.Price, deserialized.Trip.Price);
        Assert.Equal(readModel.Trip.Description, deserialized.Trip.Description);
        Assert.Equal(readModel.Trip.SeatNumber, deserialized.Trip.SeatNumber);
    }

    private static BookingReadModel CreateReadModel() =>
        new()
        {
            Id = Guid.NewGuid(),
            BookId = Guid.NewGuid(),
            IsDeleted = false,
            PassengerInfo = PassengerInfo.Of("Test passenger"),
            Trip = Trip.Of(
                "AB123",
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                new DateTime(2030, 4, 5, 12, 30, 0, DateTimeKind.Utc),
                249.99m,
                "Window seat",
                "12A"
            ),
        };

    private sealed class TestOptionsSnapshot<TOptions>(TOptions value) : IOptionsSnapshot<TOptions>
        where TOptions : class
    {
        public TOptions Value => value;

        public TOptions Get(string? name) => value;
    }
}
