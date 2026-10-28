using System.Diagnostics;
using BuildingBlocks.OpenTelemetryCollector;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Xunit;

namespace Gateway.Unit.Test;

public class SuppressionAwarePropagatorTests
{
    [Theory]
    [InlineData("System.Net.Http", false)]
    [InlineData("Grpc.Net.Client", true)]
    public void Propagates_context_for_exported_http_and_suppressed_grpc(string sourceName, bool suppress)
    {
        using var listener = Listen(sourceName);
        using var source = new ActivitySource(sourceName);
        using var activity = source.StartActivity("request")!;
        using var scope = SuppressInstrumentationScope.Begin(suppress);
        var headers = new Dictionary<string, string>();

        new SuppressionAwarePropagator().Inject(
            new PropagationContext(activity.Context, default),
            headers,
            (carrier, key, value) => carrier[key] = value
        );

        Assert.Equal(activity.Id, headers["traceparent"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Suppressed_http_does_not_overwrite_grpc_parent(bool activitySource)
    {
        using var parent = new Activity("grpc").Start();
        using var listener = Listen("System.Net.Http");
        using var source = new ActivitySource("System.Net.Http");
        using var http = activitySource
            ? source.StartActivity("System.Net.Http.HttpRequestOut")!
            : new Activity("System.Net.Http.HttpRequestOut").Start();
        using var scope = SuppressInstrumentationScope.Begin();
        var headers = new Dictionary<string, string> { ["traceparent"] = parent.Id! };

        new SuppressionAwarePropagator().Inject(
            new PropagationContext(http.Context, default),
            headers,
            (carrier, key, value) => carrier[key] = value
        );

        Assert.Equal(parent.Id, headers["traceparent"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("00-ab495000000000000000000000000001-ab49500000000001-01")]
    public void Extraction_preserves_w3c_and_baggage(string? traceparent)
    {
        var headers = new Dictionary<string, string> { ["baggage"] = "correlation.id=proof" };
        if (traceparent is not null)
            headers["traceparent"] = traceparent;

        var context = new SuppressionAwarePropagator().Extract(
            default,
            headers,
            (carrier, key) => carrier.TryGetValue(key, out var value) ? [value] : []
        );

        Assert.Equal("proof", context.Baggage.GetBaggage("correlation.id"));
        Assert.Equal(
            traceparent?.StartsWith("00-", StringComparison.Ordinal) == true,
            context.ActivityContext != default
        );
    }

    private static ActivityListener Listen(string sourceName)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
