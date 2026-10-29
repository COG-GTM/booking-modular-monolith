using System.Diagnostics;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.PersistMessageProcessor;

// Carries the W3C trace context of the request that wrote an outbox/internal message across the store,
// so the background worker's publish joins the original distributed trace instead of starting a new one.
public static class PersistMessageTracing
{
    public const string ActivitySourceName = "BuildingBlocks.PersistMessageProcessor";
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static bool IsTraceHeader(string key) =>
        string.Equals(key, TraceParentHeader, StringComparison.OrdinalIgnoreCase)
        || string.Equals(key, TraceStateHeader, StringComparison.OrdinalIgnoreCase);

    public static MessageEnvelope WithCurrentTraceContext(MessageEnvelope envelope, Activity? current = null)
    {
        current ??= Activity.Current;
        if (current is null || current.IdFormat != ActivityIdFormat.W3C || envelope.Headers.ContainsKey(TraceParentHeader))
            return envelope;

        var headers = new Dictionary<string, object?>(envelope.Headers) { [TraceParentHeader] = current.Id };
        if (!string.IsNullOrEmpty(current.TraceStateString))
            headers[TraceStateHeader] = current.TraceStateString;

        return new MessageEnvelope(envelope.Message, headers);
    }

    public static ActivityContext GetParentContext(IDictionary<string, object?> headers)
    {
        if (
            headers.TryGetValue(TraceParentHeader, out var traceParent)
            && ActivityContext.TryParse(
                traceParent?.ToString(),
                headers.TryGetValue(TraceStateHeader, out var traceState) ? traceState?.ToString() : null,
                isRemote: true,
                out var context
            )
        )
            return context;

        return default;
    }

    public static Activity? StartProcessActivity(
        string name,
        ActivityKind kind,
        PersistMessage message,
        IDictionary<string, object?> headers
    )
    {
        var activity = ActivitySource.StartActivity(name, kind, GetParentContext(headers));
        activity?.SetTag("messaging.message.id", message.Id.ToString());
        activity?.SetTag("messaging.message.type", message.DataType);
        activity?.SetTag("persist_message.delivery_type", message.DeliveryType.ToString());
        activity?.SetTag("persist_message.retry_count", message.RetryCount);
        return activity;
    }
}
