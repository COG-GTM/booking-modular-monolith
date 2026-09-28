using System.Linq;
using FluentAssertions;
using Google.Protobuf.Reflection;
using Xunit;

namespace Contract.Test.Grpc;

/// <summary>
/// Booking ships its own copy of passenger.proto (same <c>passenger</c> package, C# namespace
/// <c>BookingPassenger</c>). Before the split the consumer copy declared <c>package bookingPassenger</c>, so the
/// generated stub called <c>/bookingPassenger.PassengerGrpcService/*</c> — a path the provider never mapped. These
/// tests pin the consumer copy to the provider contract owned by Passenger so that cannot happen again.
/// </summary>
public class PassengerGrpcSchemaCompatibilityTests
{
    private static readonly ServiceDescriptor Provider =
        global::Passenger.PassengerReflection.Descriptor.Services.Single();
    private static readonly ServiceDescriptor Consumer =
        BookingPassenger.PassengerReflection.Descriptor.Services.Single();

    [Fact]
    public void consumer_and_provider_expose_the_same_service_name()
    {
        Consumer.FullName.Should().Be(Provider.FullName);
        Provider.FullName.Should().Be("passenger.PassengerGrpcService");
    }

    [Fact]
    public void consumer_stub_targets_the_provider_method_path()
    {
        BookingPassenger.PassengerGrpcService.Descriptor.FullName.Should().Be("passenger.PassengerGrpcService");

        // The C# namespace is the only thing that may differ between the two copies.
        typeof(BookingPassenger.PassengerGrpcService.PassengerGrpcServiceClient)
            .Namespace.Should()
            .Be("BookingPassenger");
        typeof(global::Passenger.PassengerGrpcService.PassengerGrpcServiceBase).Namespace.Should().Be("Passenger");
    }

    [Fact]
    public void every_method_booking_calls_exists_on_passenger()
    {
        Consumer.Methods.Select(m => m.Name).Should().BeSubsetOf(Provider.Methods.Select(m => m.Name));
        Consumer.Methods.Select(m => m.Name).Should().Contain(["GetById"]);
    }

    [Theory]
    [InlineData("GetById")]
    public void request_and_response_messages_are_wire_compatible(string methodName)
    {
        GrpcSchemaCompatibility.AssertMethodCompatible(
            Consumer.FindMethodByName(methodName),
            Provider.FindMethodByName(methodName)
        );
    }

    [Fact]
    public void whole_service_is_wire_compatible()
    {
        GrpcSchemaCompatibility.AssertServiceCompatible(Consumer, Provider);
    }
}
