using BuildingBlocks.Core.Event;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace BuildingBlocks.Unit.Test.CoreTests;

public class MessageEnvelopeTests
{
    [Fact]
    public void null_headers_yield_an_empty_header_dictionary()
    {
        var envelope = new MessageEnvelope("payload");

        envelope.Message.Should().Be("payload");
        envelope.Headers.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void typed_envelope_exposes_the_typed_message_and_the_same_base_instance()
    {
        var message = new StringValue { Value = "hello" };
        var headers = new Dictionary<string, object?> { ["k"] = "v" };

        var envelope = new MessageEnvelope<StringValue>(message, headers);

        envelope.Message.Should().BeSameAs(message);
        ((MessageEnvelope)envelope).Message.Should().BeSameAs(message);
        envelope.Headers.Should().ContainKey("k").WhoseValue.Should().Be("v");
    }
}
