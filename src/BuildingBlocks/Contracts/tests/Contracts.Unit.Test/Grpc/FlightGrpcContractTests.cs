using Flight;
using Google.Protobuf.Reflection;
using Xunit;

namespace Contracts.Unit.Test.Grpc;

public class FlightGrpcContractTests
{
    [Fact]
    public void flight_service_should_have_expected_full_name()
    {
        Assert.Equal("flight.FlightGrpcService", FlightGrpcService.Descriptor.FullName);
    }

    [Theory]
    [InlineData("GetById")]
    [InlineData("GetAvailableSeats")]
    [InlineData("ReserveSeat")]
    public void flight_service_should_expose_method(string methodName)
    {
        Assert.NotNull(FlightGrpcService.Descriptor.FindMethodByName(methodName));
    }

    [Fact]
    public void get_by_id_should_map_request_and_response_types()
    {
        var method = FlightGrpcService.Descriptor.FindMethodByName("GetById");

        Assert.Equal("flight.GetByIdRequest", method.InputType.FullName);
        Assert.Equal("flight.GetFlightByIdResult", method.OutputType.FullName);
    }

    [Fact]
    public void flight_response_should_keep_expected_fields()
    {
        var fields = FlightResponse.Descriptor.Fields.InDeclarationOrder().Select(f => f.Name).ToList();

        Assert.Equal(
            new[]
            {
                "Id", "FlightNumber", "AircraftId", "DepartureAirportId", "DepartureDate", "ArriveDate",
                "ArriveAirportId", "DurationMinutes", "FlightDate", "Status", "Price", "FlightId",
            },
            fields);
    }

    [Fact]
    public void flight_status_enum_should_keep_expected_values()
    {
        Assert.Equal(0, (int)FlightStatus.Unknown);
        Assert.Equal(1, (int)FlightStatus.Flying);
        Assert.Equal(2, (int)FlightStatus.Delay);
        Assert.Equal(3, (int)FlightStatus.Canceled);
        Assert.Equal(4, (int)FlightStatus.Completed);
    }

    [Fact]
    public void generated_client_should_be_available_from_shared_contracts()
    {
        Assert.Equal("Grpc.Contracts", typeof(FlightGrpcService.FlightGrpcServiceClient).Assembly.GetName().Name);
    }
}
