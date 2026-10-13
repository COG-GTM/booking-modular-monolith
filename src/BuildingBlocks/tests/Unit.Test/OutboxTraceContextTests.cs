using System.Diagnostics;
using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class OutboxTraceContextTests : IDisposable
{
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public OutboxTraceContextTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void outbox_publish_continues_the_trace_that_wrote_the_message()
    {
        using var source = new ActivitySource("test");
        var envelope = new MessageEnvelope(new { Id = 1 });
        string traceId;

        using (var request = source.StartActivity("POST /api/v1.0/booking"))
        {
            traceId = request!.TraceId.ToString();
            PersistMessageProcessor.PersistMessageProcessor.CaptureTraceContext(envelope);
        }

        // round-trip through the outbox row, as the background worker reads it
        var stored = JsonSerializer.Deserialize<MessageEnvelope>(JsonSerializer.Serialize(envelope))!;
        var message = new PersistMessage(Guid.NewGuid(), "Event", "{}", MessageDeliveryType.Outbox);

        using var publish = PersistMessageProcessor.PersistMessageProcessor.StartOutboxPublishActivity(message, stored);

        publish.Should().NotBeNull();
        publish!.TraceId.ToString().Should().Be(traceId);
        publish.Source.Name.Should().Be(PersistMessageProcessor.PersistMessageProcessor.ActivitySourceName);
    }

    [Fact]
    public void existing_traceparent_header_is_not_overwritten()
    {
        using var source = new ActivitySource("test");
        var envelope = new MessageEnvelope(
            new { Id = 1 },
            new Dictionary<string, object?> { ["traceparent"] = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01" }
        );

        using (source.StartActivity("other"))
            PersistMessageProcessor.PersistMessageProcessor.CaptureTraceContext(envelope);

        envelope.Headers["traceparent"].Should().Be("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01");
    }

    [Fact]
    public void message_without_trace_context_starts_a_new_root()
    {
        var envelope = new MessageEnvelope(new { Id = 1 });
        var message = new PersistMessage(Guid.NewGuid(), "Event", "{}", MessageDeliveryType.Outbox);

        using var publish = PersistMessageProcessor.PersistMessageProcessor.StartOutboxPublishActivity(message, envelope);

        publish!.ParentSpanId.Should().Be(default(ActivitySpanId));
    }
}
