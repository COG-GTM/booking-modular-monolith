using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Gateway.Unit.Test;

public class GatewayObservabilityTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("invalid")]
    [InlineData("4d4d8113-18f9-42c4-bcaf-b64308996e61")]
    public async Task Correlation_is_attached_to_request_span_and_baggage(string? correlation)
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        await using var app = new WebApplicationFactory<Program>();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/flight/observability-test");
        if (correlation is not null)
            request.Headers.Add("X-Correlation-ID", correlation);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var returned = response.Headers.GetValues("X-Correlation-ID").Single();
        Assert.True(Guid.TryParse(returned, out _));
        if (Guid.TryParse(correlation, out var supplied))
            Assert.Equal(supplied.ToString(), returned);
        var span = Assert.Single(stopped, activity => Equals(activity.GetTagItem("correlation.id"), returned));
        Assert.Equal(returned, span.GetBaggageItem("correlation.id"));
    }

    [Theory]
    [InlineData("live", "/health/live", HttpStatusCode.ServiceUnavailable)]
    [InlineData("ready", "/health/live", HttpStatusCode.OK)]
    [InlineData(null, "/health/live", HttpStatusCode.OK)]
    [InlineData("ready", "/health/ready", HttpStatusCode.ServiceUnavailable)]
    public async Task Liveness_runs_only_live_tagged_checks(string? tag, string path, HttpStatusCode expected)
    {
        var calls = 0;
        await using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services
                    .AddHealthChecks()
                    .AddCheck(
                        "failing-test-check",
                        () =>
                        {
                            Interlocked.Increment(ref calls);
                            return HealthCheckResult.Unhealthy("test failure");
                        },
                        tags: tag is null ? [] : [tag]
                    )
            )
        );
        using var client = app.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.ServiceUnavailable ? 1 : 0, calls);
    }
}
