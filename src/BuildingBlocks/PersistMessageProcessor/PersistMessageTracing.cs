using System.Diagnostics;

namespace BuildingBlocks.PersistMessageProcessor;

public static class PersistMessageTracing
{
    public const string ActivitySourceName = "BuildingBlocks.PersistMessageProcessor";
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
