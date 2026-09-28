using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Google.Protobuf.Reflection;

namespace Contract.Test.Grpc;

/// <summary>
/// Descriptor-level comparison of a consumer's copy of a gRPC contract against the provider's original: same fully
/// qualified service name, every consumed method present with matching streaming shape, and request/response messages
/// wire compatible (field numbers, types, cardinality, names and enum values).
/// </summary>
public static class GrpcSchemaCompatibility
{
    public static void AssertServiceCompatible(ServiceDescriptor consumer, ServiceDescriptor provider)
    {
        consumer.FullName.Should().Be(provider.FullName);
        consumer.Methods.Select(m => m.Name).Should().BeSubsetOf(provider.Methods.Select(m => m.Name));

        foreach (var consumerMethod in consumer.Methods)
        {
            AssertMethodCompatible(consumerMethod, provider.FindMethodByName(consumerMethod.Name));
        }
    }

    public static void AssertMethodCompatible(MethodDescriptor consumerMethod, MethodDescriptor providerMethod)
    {
        providerMethod
            .Should()
            .NotBeNull("method {0} is called by the consumer but missing on the provider", consumerMethod.Name);

        consumerMethod.IsClientStreaming.Should().Be(providerMethod!.IsClientStreaming);
        consumerMethod.IsServerStreaming.Should().Be(providerMethod.IsServerStreaming);

        AssertWireCompatible(consumerMethod.InputType, providerMethod.InputType, []);
        AssertWireCompatible(consumerMethod.OutputType, providerMethod.OutputType, []);
    }

    public static void AssertWireCompatible(
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
                    "field {0}.{1} (#{2}) is used by the consumer but missing on the provider",
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
