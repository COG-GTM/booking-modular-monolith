using BuildingBlocks.OpenTelemetryCollector;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Gateway.Unit.Test;

public class ObservabilityMiddlewareTests
{
    [Theory]
    [InlineData("/health", true)]
    [InlineData("/health/live", true)]
    [InlineData("/health/ready", true)]
    [InlineData("/HEALTH/ready", true)]
    [InlineData("/healthz", true)]
    [InlineData("/metrics", true)]
    [InlineData("/metrics/details", true)]
    [InlineData("/alive", true)]
    [InlineData("/alive/details", true)]
    [InlineData("/", false)]
    [InlineData("/api/bookings", false)]
    [InlineData("/metrics-other", false)]
    [InlineData("/error", false)]
    public async Task Diagnostic_requests_disable_metrics(string path, bool expected)
    {
        await using var app = await CreateApp(true);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(expected.ToString(), await response.Content.ReadAsStringAsync());
        Assert.Equal(path == "/error" ? 500 : 200, (int)response.StatusCode);
    }

    [Fact]
    public async Task Missing_metrics_feature_does_not_break_health_requests()
    {
        await using var app = await CreateApp(false);
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/health/ready");

        response.EnsureSuccessStatusCode();
        Assert.Equal(bool.FalseString, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Development", "http://localhost:4317")]
    [InlineData("docker", "http://otel-collector:4317")]
    public void Gateway_uses_environment_specific_collector_endpoint(string environment, string endpoint)
    {
        using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseEnvironment(environment)
        );
        var configuration = app.Services.GetRequiredService<IConfiguration>();

        Assert.Equal(endpoint, configuration["ObservabilityOptions:OTLPOptions:OTLPGrpcExporterEndpoint"]);
    }

    private static async Task<WebApplication> CreateApp(bool includeFeature)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.Configure<ObservabilityOptions>(options => options.UsePrometheusExporter = false);
        var app = builder.Build();
        app.Use(
            async (context, next) =>
            {
                context.Features.Set<IHttpMetricsTagsFeature>(includeFeature ? new MetricsTagsFeature() : null);
                await next(context);
            }
        );
        app.UseCustomObservability();
        app.Run(context =>
        {
            context.Response.StatusCode = context.Request.Path == "/error" ? 500 : 200;
            var disabled = context.Features.Get<IHttpMetricsTagsFeature>()?.MetricsDisabled == true;
            return context.Response.WriteAsync(disabled.ToString());
        });
        await app.StartAsync();
        return app;
    }

    private sealed class MetricsTagsFeature : IHttpMetricsTagsFeature
    {
        public ICollection<KeyValuePair<string, object?>> Tags { get; } = new List<KeyValuePair<string, object?>>();
        public bool MetricsDisabled { get; set; }
    }
}
