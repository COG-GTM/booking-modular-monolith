using System.Linq;
using FluentAssertions;
using Google.Protobuf.Reflection;
using Xunit;

namespace Contract.Test.Grpc;

/// <summary>
/// Booking ships its own copy of flight.proto (same <c>flight</c> package, C# namespace <c>BookingFlight</c>). These
/// tests pin the consumer copy to the provider contract owned by Flight so the two cannot drift apart silently.
/// </summary>
public class FlightGrpcSchemaCompatibilityTests
{
    private static readonly ServiceDescriptor Provider = global::Flight.FlightReflection.Descriptor.Services.Single();
    private static readonly ServiceDescriptor Consumer = BookingFlight.FlightReflection.Descriptor.Services.Single();

    [Fact]
    public void consumer_and_provider_expose_the_same_service_name()
    {
        Consumer.FullName.Should().Be(Provider.FullName);
        Provider.FullName.Should().Be("flight.FlightGrpcService");
    }

    [Fact]
    public void every_method_booking_calls_exists_on_flight()
    {
        Consumer.Methods.Select(m => m.Name).Should().BeSubsetOf(Provider.Methods.Select(m => m.Name));
        Consumer.Methods.Select(m => m.Name).Should().Contain(["GetById", "GetAvailableSeats", "ReserveSeat"]);
    }

    [Theory]
    [InlineData("GetById")]
    [InlineData("GetAvailableSeats")]
    [InlineData("ReserveSeat")]
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
