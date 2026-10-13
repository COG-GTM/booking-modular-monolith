using BuildingBlocks.Contracts.Grpc.Flight;
using BuildingBlocks.Contracts.Grpc.Passenger;
using FluentAssertions;
using Xunit;

namespace Unit.Test.Contracts;

// The service hosts and the Booking gRPC clients must agree on the wire-level service names; the shared
// protos in BuildingBlocks replaced module-owned copies whose packages (bookingFlight/bookingPassenger)
// did not match the servers (flight/passenger).
public class SharedGrpcContractTests
{
    [Fact]
    public void flight_contract_should_use_the_flight_package_on_the_wire()
    {
        FlightGrpcService.Descriptor.FullName.Should().Be("flight.FlightGrpcService");
        FlightGrpcService
            .Descriptor.Methods.Select(x => x.Name)
            .Should()
            .BeEquivalentTo("GetById", "GetAvailableSeats", "ReserveSeat");
    }

    [Fact]
    public void passenger_contract_should_use_the_passenger_package_on_the_wire()
    {
        PassengerGrpcService.Descriptor.FullName.Should().Be("passenger.PassengerGrpcService");
        PassengerGrpcService.Descriptor.Methods.Select(x => x.Name).Should().BeEquivalentTo("GetById");
    }

    [Fact]
    public void shared_contracts_should_generate_both_client_and_server_in_building_blocks()
    {
        var buildingBlocks = typeof(BuildingBlocks.Core.Event.MessageEnvelope).Assembly;

        typeof(FlightGrpcService.FlightGrpcServiceClient).Assembly.Should().BeSameAs(buildingBlocks);
        typeof(FlightGrpcService.FlightGrpcServiceBase).Assembly.Should().BeSameAs(buildingBlocks);
        typeof(PassengerGrpcService.PassengerGrpcServiceClient).Assembly.Should().BeSameAs(buildingBlocks);
        typeof(PassengerGrpcService.PassengerGrpcServiceBase).Assembly.Should().BeSameAs(buildingBlocks);
    }

    [Fact]
    public void flight_grpc_server_should_implement_the_shared_contract()
    {
        typeof(global::Flight.GrpcServer.Services.FlightGrpcServices)
            .BaseType.Should()
            .Be<FlightGrpcService.FlightGrpcServiceBase>();
    }
}
