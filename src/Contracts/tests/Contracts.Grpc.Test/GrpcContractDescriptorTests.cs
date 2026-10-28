using Contracts.Grpc.Flight;
using FluentAssertions;
using Xunit;
using FlightGrpcService = Contracts.Grpc.Flight.FlightGrpcService;
using PassengerGrpcService = Contracts.Grpc.Passenger.PassengerGrpcService;

namespace Contracts.Grpc.Test;

public class GrpcContractDescriptorTests
{
    [Fact]
    public void flight_service_descriptor_preserves_wire_contract()
    {
        FlightReflection.Descriptor.Package.Should().Be("flight");
        FlightReflection.Descriptor.Services.Single().FullName.Should().Be("flight.FlightGrpcService");
        FlightReflection
            .Descriptor.Services.Single()
            .Methods.Select(method => method.Name)
            .Should()
            .BeEquivalentTo(["GetById", "GetAvailableSeats", "ReserveSeat"]);
        typeof(FlightGrpcService).Namespace.Should().Be("Contracts.Grpc.Flight");
        typeof(Contracts.Grpc.Passenger.PassengerGrpcService).Namespace.Should().Be("Contracts.Grpc.Passenger");

        AssertFields<GetByIdRequest>(("Id", 1));
        AssertFields<GetFlightByIdResult>(("FlightDto", 1));
        AssertFields<GetAvailableSeatsResult>(("SeatDtos", 1));
        AssertFields<ReserveSeatResult>(("Id", 1));
        AssertFields<FlightResponse>(
            ("Id", 1),
            ("FlightNumber", 2),
            ("AircraftId", 3),
            ("DepartureAirportId", 4),
            ("DepartureDate", 5),
            ("ArriveDate", 6),
            ("ArriveAirportId", 7),
            ("DurationMinutes", 8),
            ("FlightDate", 9),
            ("Status", 10),
            ("Price", 11),
            ("FlightId", 12)
        );
        AssertFields<GetAvailableSeatsRequest>(("FlightId", 1));
        AssertFields<SeatDtoResponse>(("Id", 1), ("SeatNumber", 2), ("Type", 3), ("Class", 4), ("FlightId", 5));
        AssertFields<ReserveSeatRequest>(("FlightId", 1), ("SeatNumber", 2));

        Contracts.Grpc.Passenger.PassengerReflection.Descriptor.Package.Should().Be("passenger");
        Contracts
            .Grpc.Passenger.PassengerReflection.Descriptor.Services.Single()
            .FullName.Should()
            .Be("passenger.PassengerGrpcService");
        Contracts
            .Grpc.Passenger.PassengerReflection.Descriptor.Services.Single()
            .Methods.Select(method => method.Name)
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be("GetById");
        AssertFields<Contracts.Grpc.Passenger.GetByIdRequest>(("Id", 1));
        AssertFields<Contracts.Grpc.Passenger.GetPassengerByIdResult>(("PassengerDto", 1));
        AssertFields<Contracts.Grpc.Passenger.PassengerResponse>(
            ("Id", 1),
            ("Name", 2),
            ("PassportNumber", 3),
            ("PassengerType", 4),
            ("Age", 5),
            ("Email", 6)
        );
    }

    private static void AssertFields<TMessage>(params (string Name, int Number)[] expected)
        where TMessage : Google.Protobuf.IMessage<TMessage>, new()
    {
        var descriptor = new TMessage().Descriptor;
        var actual = new List<KeyValuePair<string, int>>();
        foreach (var field in descriptor.Fields.InFieldNumberOrder())
        {
            actual.Add(new KeyValuePair<string, int>(field.Name, field.FieldNumber));
        }

        var expectedFields = new List<KeyValuePair<string, int>>();
        foreach (var field in expected)
        {
            expectedFields.Add(new KeyValuePair<string, int>(field.Name, field.Number));
        }

        actual.Should().BeEquivalentTo(expectedFields);
    }
}
