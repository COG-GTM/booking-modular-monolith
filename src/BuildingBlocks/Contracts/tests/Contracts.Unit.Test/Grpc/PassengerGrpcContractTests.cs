using Google.Protobuf.Reflection;
using Passenger;
using Xunit;

namespace Contracts.Unit.Test.Grpc;

public class PassengerGrpcContractTests
{
    [Fact]
    public void passenger_service_should_have_expected_full_name()
    {
        Assert.Equal("passenger.PassengerGrpcService", PassengerGrpcService.Descriptor.FullName);
    }

    [Fact]
    public void get_by_id_should_map_request_and_response_types()
    {
        var method = PassengerGrpcService.Descriptor.FindMethodByName("GetById");

        Assert.NotNull(method);
        Assert.Equal("passenger.GetByIdRequest", method.InputType.FullName);
        Assert.Equal("passenger.GetPassengerByIdResult", method.OutputType.FullName);
    }

    [Fact]
    public void passenger_response_should_keep_expected_fields()
    {
        var fields = PassengerResponse.Descriptor.Fields.InDeclarationOrder().Select(f => f.Name).ToList();

        Assert.Equal(new[] { "Id", "Name", "PassportNumber", "PassengerType", "Age", "Email" }, fields);
    }

    [Fact]
    public void passenger_type_enum_should_keep_expected_values()
    {
        Assert.Equal(0, (int)PassengerType.Unknown);
        Assert.Equal(1, (int)PassengerType.Male);
        Assert.Equal(2, (int)PassengerType.Female);
        Assert.Equal(3, (int)PassengerType.Baby);
    }

    [Fact]
    public void generated_client_should_be_available_from_shared_contracts()
    {
        Assert.Equal("Grpc.Contracts", typeof(PassengerGrpcService.PassengerGrpcServiceClient).Assembly.GetName().Name);
    }
}
