using System.Diagnostics.Metrics;
using System.Text.Json;
using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests;

public class HealthTelemetryTests
{
    [Fact]
    public async Task gauge_starts_empty_and_updates_a_check_to_degraded()
    {
        using var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var factory = services.GetRequiredService<IMeterFactory>();
        using var publisher = new HealthCheckMetricsPublisher(factory);
        var values = new List<double>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, owner) =>
        {
            if (instrument.Meter == factory.Create(HealthCheckMetricsPublisher.MeterName))
                owner.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<double>(
            (instrument, value, tags, _) =>
            {
                instrument.Name.Should().Be(HealthCheckMetricsPublisher.StatusInstrumentName);
                tags.ToArray()
                    .Should()
                    .ContainSingle()
                    .Which.Should()
                    .Be(new KeyValuePair<string, object?>("check", "database"));
                values.Add(value);
            }
        );
        listener.Start();
        listener.RecordObservableInstruments();
        values.Should().BeEmpty();

        foreach (var status in new[] { HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Unhealthy })
        {
            await publisher.PublishAsync(
                new HealthReport(
                    new Dictionary<string, HealthReportEntry>
                    {
                        ["database"] = new(status, null, TimeSpan.Zero, null, null),
                    },
                    TimeSpan.Zero
                ),
                CancellationToken.None
            );
            listener.RecordObservableInstruments();
        }

        values.Should().Equal(1d, 0.5d, 0d);
        publisher.Latest.Should().ContainSingle().Which.Value.Should().Be(HealthStatus.Unhealthy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void metrics_toggle_controls_publisher_registration_without_disabling_self_check(bool metricsEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["HealthOptions:Enabled"] = "false",
                    ["HealthOptions:MetricsEnabled"] = metricsEnabled.ToString(),
                }
            )
            .Build();
        var collection = new ServiceCollection();
        collection.AddSingleton<IConfiguration>(configuration);
        collection.AddCustomHealthCheck();
        using var services = collection.BuildServiceProvider();

        services
            .GetServices<IHealthCheckPublisher>()
            .OfType<HealthCheckMetricsPublisher>()
            .Count()
            .Should()
            .Be(metricsEnabled ? 1 : 0);
        services
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Should()
            .ContainSingle(r => r.Name == "self");
    }

    [Theory]
    [InlineData(false, "Development")]
    [InlineData(false, "Production")]
    [InlineData(false, "docker")]
    [InlineData(false, null)]
    [InlineData(true, "Production")]
    public async Task response_contains_only_status_and_safe_check_details(bool empty, string? environmentName)
    {
        var entries = new Dictionary<string, HealthReportEntry>();
        if (!empty)
            entries["database"] = new(
                HealthStatus.Degraded,
                "slow",
                TimeSpan.FromSeconds(1),
                new System.Exception("private"),
                new Dictionary<string, object> { ["secret"] = "hidden" }
            );
        var context = new DefaultHttpContext();
        var collection = new ServiceCollection();
        if (environmentName is not null)
        {
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(environmentName);
            collection.AddSingleton(environment);
        }
        using var services = collection.BuildServiceProvider();
        context.RequestServices = services;
        await using var body = new MemoryStream();
        context.Response.Body = body;

        await HealthResponseWriter.WriteAsync(context, new HealthReport(entries, TimeSpan.Zero));

        context.Response.ContentType.Should().StartWith("application/json");
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        json.RootElement.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("status", "checks");
        json.RootElement.GetProperty("status").GetString().Should().Be(empty ? "Healthy" : "Degraded");
        var checks = json.RootElement.GetProperty("checks");
        if (empty)
            checks.EnumerateObject().Should().BeEmpty();
        else
        {
            var check = checks.GetProperty("database");
            check.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("status", "description");
            check.GetProperty("status").GetString().Should().Be("Degraded");
            check.GetProperty("description").GetString().Should().Be(environmentName == "Development" ? "slow" : null);
        }
    }
}
