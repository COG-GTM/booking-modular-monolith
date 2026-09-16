namespace Unit.Test.Mongo;

using System;
using System.Linq;
using BuildingBlocks.Mongo;
using FluentAssertions;
using global::Booking.Booking.ValueObjects;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class ImmutablePocoConventionTests
{
    [Fact]
    public void should_map_private_constructor_and_read_only_properties_of_trip()
    {
        // Arrange
        var classMap = new BsonClassMap<Trip>();

        // Act
        ApplyConventions(classMap);

        // Assert
        classMap.CreatorMaps.Should().ContainSingle();
        classMap.CreatorMaps.Single().Arguments.Should().HaveCount(8);
        classMap.DeclaredMemberMaps.Select(x => x.MemberName).Should().BeEquivalentTo(
            nameof(Trip.FlightNumber),
            nameof(Trip.AircraftId),
            nameof(Trip.DepartureAirportId),
            nameof(Trip.ArriveAirportId),
            nameof(Trip.FlightDate),
            nameof(Trip.Price),
            nameof(Trip.Description),
            nameof(Trip.SeatNumber));
    }

    [Fact]
    public void should_map_private_constructor_and_read_only_properties_of_passenger_info()
    {
        // Arrange
        var classMap = new BsonClassMap<PassengerInfo>();

        // Act
        ApplyConventions(classMap);

        // Assert
        classMap.CreatorMaps.Should().ContainSingle();
        classMap.CreatorMaps.Single().Arguments.Should().HaveCount(1);
        classMap.DeclaredMemberMaps.Select(x => x.MemberName).Should().BeEquivalentTo(nameof(PassengerInfo.Name));
    }

    [Fact]
    public void should_round_trip_trip_with_private_constructor()
    {
        // Arrange
        var trip = FakeTrip.Generate();
        var serializer = CreateSerializer<Trip>();

        // Act
        var document = Serialize(serializer, trip);
        var deserialized = Deserialize(serializer, document);

        // Assert
        deserialized.Should().Be(trip);
        deserialized.FlightNumber.Should().Be("1500B");
        deserialized.SeatNumber.Should().Be("33F");
    }

    [Fact]
    public void should_round_trip_passenger_info_with_private_constructor()
    {
        // Arrange
        var passengerInfo = PassengerInfo.Of("Sam");
        var serializer = CreateSerializer<PassengerInfo>();

        // Act
        var document = Serialize(serializer, passengerInfo);
        var deserialized = Deserialize(serializer, document);

        // Assert
        deserialized.Should().Be(passengerInfo);
        deserialized.Name.Should().Be("Sam");
    }

    private static void ApplyConventions(BsonClassMap classMap)
    {
        var conventions = new ConventionPack { new ImmutablePocoConvention(), new NamedParameterCreatorMapConvention() };
        new ConventionRunner(conventions).Apply(classMap);
    }

    private static BsonClassMapSerializer<T> CreateSerializer<T>()
    {
        var classMap = new BsonClassMap<T>();
        ApplyConventions(classMap);

        foreach (var memberMap in classMap.DeclaredMemberMaps.Where(x => x.MemberType == typeof(Guid)))
        {
            memberMap.SetSerializer(new GuidSerializer(BsonType.String));
        }

        classMap.Freeze();

        return new BsonClassMapSerializer<T>(classMap);
    }

    private static BsonDocument Serialize<T>(IBsonSerializer<T> serializer, T value)
    {
        var document = new BsonDocument();
        using var writer = new BsonDocumentWriter(document);
        serializer.Serialize(BsonSerializationContext.CreateRoot(writer), value);

        return document;
    }

    private static T Deserialize<T>(IBsonSerializer<T> serializer, BsonDocument document)
    {
        using var reader = new BsonDocumentReader(document);

        return serializer.Deserialize(BsonDeserializationContext.CreateRoot(reader));
    }
}
