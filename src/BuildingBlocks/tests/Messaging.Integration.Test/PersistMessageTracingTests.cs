using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using Xunit;

namespace Messaging.Integration.Test;

public class PersistMessageTracingTests
{
    [Theory]
    [InlineData(MessageDeliveryType.Outbox)]
    [InlineData(MessageDeliveryType.Internal)]
    public void Stored_envelope_restores_original_parent(MessageDeliveryType deliveryType)
    {
        using var listener = Listen();
        var envelope = new MessageEnvelope(new { Value = "message" });
        ActivityTraceId traceId;
        ActivitySpanId spanId;
        using (var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            request.TraceStateString = "vendor=value";
            traceId = request.TraceId;
            spanId = request.SpanId;
            PersistMessageTracing.Capture(envelope);
        }
        var stored = new PersistMessage(
            Guid.NewGuid(),
            "TestMessage",
            JsonSerializer.Serialize(envelope),
            deliveryType
        );
        var restored = JsonSerializer.Deserialize<MessageEnvelope>(stored.Data)!;
        using var publish = PersistMessageTracing.Start(restored, stored.DataType, deliveryType);
        Assert.NotNull(publish);
        Assert.Equal(traceId, publish.TraceId);
        Assert.Equal(spanId, publish.ParentSpanId);
        Assert.Equal("vendor=value", publish.TraceStateString);
        Assert.Equal(ActivityKind.Producer, publish.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-traceparent")]
    public void Legacy_or_invalid_metadata_does_not_fail_dispatch(string? traceparent)
    {
        using var listener = Listen();
        var envelope = new MessageEnvelope(new object());
        envelope.Headers["traceparent"] = traceparent;
        using var publish = PersistMessageTracing.Start(envelope, "TestMessage", MessageDeliveryType.Outbox);
        Assert.NotNull(publish);
        Assert.Equal(default, publish.ParentSpanId);
    }

    [Fact]
    public void Capture_without_activity_leaves_metadata_empty()
    {
        var envelope = new MessageEnvelope(new object());
        PersistMessageTracing.Capture(envelope);
        Assert.Empty(envelope.Headers);
    }

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PersistMessageTracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
