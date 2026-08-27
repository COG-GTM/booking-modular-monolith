using System.Linq;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using Flight.GrpcServer.Services;
using Xunit;

namespace Unit.Test.Contracts;

public class GrpcWireContractTests
{
    [Fact]
    public void flight_service_should_use_flight_wire_package()
    {
        var descriptor = FlightGrpcService.Descriptor;

        Assert.Equal("flight.FlightGrpcService", descriptor.FullName);
        Assert.Equal("flight", descriptor.File.Package);
    }

    [Fact]
    public void passenger_service_should_use_passenger_wire_package()
    {
        var descriptor = PassengerGrpcService.Descriptor;

        Assert.Equal("passenger.PassengerGrpcService", descriptor.FullName);
        Assert.Equal("passenger", descriptor.File.Package);
    }

    [Theory]
    [InlineData("GetById")]
    [InlineData("GetAvailableSeats")]
    [InlineData("ReserveSeat")]
    public void flight_service_should_expose_expected_rpc_methods(string methodName)
    {
        var method = FlightGrpcService.Descriptor.Methods.SingleOrDefault(m => m.Name == methodName);

        Assert.NotNull(method);
        Assert.Equal($"flight.FlightGrpcService.{methodName}", method.FullName);
    }

    [Fact]
    public void passenger_service_should_expose_get_by_id_method()
    {
        var method = PassengerGrpcService.Descriptor.Methods.SingleOrDefault(m => m.Name == "GetById");

        Assert.NotNull(method);
        Assert.Equal("passenger.PassengerGrpcService.GetById", method.FullName);
    }

    [Fact]
    public void flight_grpc_server_should_implement_canonical_contract()
    {
        Assert.True(typeof(FlightGrpcService.FlightGrpcServiceBase).IsAssignableFrom(typeof(FlightGrpcServices)));
    }

    [Fact]
    public void flight_generated_types_should_live_in_contracts_namespace()
    {
        Assert.Equal("Contracts.Grpc.Flight", typeof(FlightGrpcService).Namespace);
        Assert.Equal("Contracts.Grpc.Flight", typeof(FlightResponse).Namespace);
        Assert.Equal("Contracts.Grpc.Passenger", typeof(PassengerResponse).Namespace);
    }
}
