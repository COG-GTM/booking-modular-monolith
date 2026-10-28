using System.Diagnostics;
using BuildingBlocks.Core.Event;

namespace BuildingBlocks.PersistMessageProcessor;

public static class PersistMessageTracing
{
    public const string SourceName = "BuildingBlocks.PersistMessageProcessor";
    private static readonly ActivitySource Source = new(SourceName);

    public static void Capture(MessageEnvelope envelope)
    {
        if (Activity.Current is not { IdFormat: ActivityIdFormat.W3C } activity)
            return;

        envelope.Headers["traceparent"] = activity.Id;
        envelope.Headers["tracestate"] = activity.TraceStateString;
    }

    public static Activity? Start(MessageEnvelope envelope, string messageType, MessageDeliveryType deliveryType)
    {
        envelope.Headers.TryGetValue("traceparent", out var traceparent);
        envelope.Headers.TryGetValue("tracestate", out var tracestate);
        ActivityContext.TryParse(traceparent?.ToString(), tracestate?.ToString(), true, out var parent);
        return Source.StartActivity(
            $"{(deliveryType == MessageDeliveryType.Outbox ? "outbox publish" : "internal dispatch")} {messageType}",
            ActivityKind.Producer,
            parent
        );
    }
}
