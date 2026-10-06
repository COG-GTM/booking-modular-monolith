using Contracts.Grpc.Flight.V1;
using Contracts.Grpc.Passenger.V1;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Contracts;

public class GrpcContractTests
{
    [Fact]
    public void flight_contract_should_be_versioned_and_shared_by_client_and_server()
    {
        FlightGrpcService.Descriptor.FullName.Should().Be("flight.v1.FlightGrpcService");
        FlightGrpcService
            .Descriptor.Methods.Select(m => m.Name)
            .Should()
            .BeEquivalentTo("GetById", "GetAvailableSeats", "ReserveSeat");

        typeof(FlightGrpcService.FlightGrpcServiceClient)
            .Namespace.Should()
            .Be(typeof(FlightGrpcService.FlightGrpcServiceBase).Namespace)
            .And.Be("Contracts.Grpc.Flight.V1");
    }

    [Fact]
    public void passenger_contract_should_be_versioned_and_shared_by_client_and_server()
    {
        PassengerGrpcService.Descriptor.FullName.Should().Be("passenger.v1.PassengerGrpcService");
        PassengerGrpcService.Descriptor.Methods.Select(m => m.Name).Should().BeEquivalentTo("GetById");

        typeof(PassengerGrpcService.PassengerGrpcServiceClient)
            .Namespace.Should()
            .Be(typeof(PassengerGrpcService.PassengerGrpcServiceBase).Namespace)
            .And.Be("Contracts.Grpc.Passenger.V1");
    }
}
