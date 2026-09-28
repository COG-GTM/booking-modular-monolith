using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Google.Protobuf.Reflection;
using Xunit;

namespace Contract.Test.Grpc;

/// <summary>
/// Booking ships its own copy of flight.proto (package <c>bookingFlight</c>). These tests pin the consumer copy to the
/// provider contract owned by Flight (package <c>flight</c>) so the two cannot drift apart silently.
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
        var consumerMethod = Consumer.FindMethodByName(methodName);
        var providerMethod = Provider.FindMethodByName(methodName);

        consumerMethod.IsClientStreaming.Should().Be(providerMethod.IsClientStreaming);
        consumerMethod.IsServerStreaming.Should().Be(providerMethod.IsServerStreaming);

        AssertWireCompatible(consumerMethod.InputType, providerMethod.InputType, []);
        AssertWireCompatible(consumerMethod.OutputType, providerMethod.OutputType, []);
    }

    // Protobuf identifies fields by number and type on the wire; names only matter for JSON transcoding, but a
    // rename on one side is almost always a mistake, so they are pinned as well.
    private static void AssertWireCompatible(
        MessageDescriptor consumer,
        MessageDescriptor provider,
        HashSet<string> visited
    )
    {
        if (!visited.Add(consumer.FullName))
            return;

        foreach (var consumerField in consumer.Fields.InDeclarationOrder())
        {
            var providerField = provider.FindFieldByNumber(consumerField.FieldNumber);

            providerField
                .Should()
                .NotBeNull(
                    "field {0}.{1} (#{2}) is used by Booking but missing on Flight",
                    consumer.Name,
                    consumerField.Name,
                    consumerField.FieldNumber
                );

            providerField!.Name.Should().Be(consumerField.Name);
            providerField.FieldType.Should().Be(consumerField.FieldType);
            providerField.IsRepeated.Should().Be(consumerField.IsRepeated);

            if (consumerField.FieldType == FieldType.Message)
            {
                AssertWireCompatible(consumerField.MessageType, providerField.MessageType, visited);
            }
            else if (consumerField.FieldType == FieldType.Enum)
            {
                consumerField
                    .EnumType.Values.Select(v => (v.Name, v.Number))
                    .Should()
                    .BeEquivalentTo(providerField.EnumType.Values.Select(v => (v.Name, v.Number)));
            }
        }
    }
}
