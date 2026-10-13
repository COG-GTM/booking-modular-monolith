using System.Collections;
using System.Net;
using System.Reflection;
using BuildingBlocks.HealthCheck;
using FluentAssertions;
using HealthChecks.UI.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class HealthCheckProbeTests
{
    [Fact]
    public void registers_only_self_when_no_configuration_is_available()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCustomHealthCheck();

        var registrations = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
        registrations.Select(r => r.Name).Should().BeEquivalentTo(Extensions.SelfCheckName);
    }

    [Fact]
    public void dependency_checks_fail_as_unhealthy_after_five_seconds()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["PostgresOptions:ConnectionString:Flight"] = "Host=db",
                        ["MongoOptions:ConnectionString"] = "mongodb://mongo:27017",
                        ["EventStoreOptions:ConnectionString"] = "esdb://eventstore:2113?tls=false",
                        ["MessageBroker:TransportType"] = "RabbitMq",
                    }
                )
                .Build()
        );

        services.AddCustomHealthCheck();

        var dependencies = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Where(r => r.Name != Extensions.SelfCheckName)
            .ToList();
        dependencies.Should().HaveCount(4);
        dependencies.Should().OnlyContain(r => r.FailureStatus == HealthStatus.Unhealthy);
        dependencies.Should().OnlyContain(r => r.Timeout == TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void health_checks_ui_is_only_registered_when_enabled_and_polls_the_readiness_probe()
    {
        var disabled = BuildServices(new() { ["HealthOptions:Enabled"] = "false" });
        using var disabledProvider = disabled.BuildServiceProvider();
        UiEndpoints(disabledProvider.GetService<IOptions<Settings>>()?.Value).Should().BeEmpty();

        var enabled = BuildServices(new() { ["HealthOptions:Enabled"] = "true", ["AppOptions:Name"] = "Flight" });
        using var enabledProvider = enabled.BuildServiceProvider();

        UiEndpoints(enabledProvider.GetRequiredService<IOptions<Settings>>().Value)
            .Should()
            .BeEquivalentTo([("Self Check - Flight", Extensions.ReadinessEndpointPath)]);
    }

    // Settings.HealthChecks and HealthCheckSetting are not public in AspNetCore.HealthChecks.UI.
    private static List<(string Name, string Uri)> UiEndpoints(Settings? settings)
    {
        if (settings is null)
            return [];

        const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var entries = (IEnumerable)typeof(Settings).GetProperty("HealthChecks", any)!.GetValue(settings)!;

        return entries
            .Cast<object>()
            .Select(e =>
                (
                    (string)e.GetType().GetProperty("Name", any)!.GetValue(e)!,
                    (string)e.GetType().GetProperty("Uri", any)!.GetValue(e)!
                )
            )
            .ToList();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task health_ui_route_is_mapped_only_when_enabled(bool enabled)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration["HealthOptions:Enabled"] = enabled.ToString();
        builder.Configuration["HealthOptions:DependencyChecksEnabled"] = "false";
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddCustomHealthCheck();

        await using var app = builder.Build();
        app.UseCustomHealthCheck();

        var routes = ((IEndpointRouteBuilder)app)
            .DataSources.SelectMany(s => s.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText?.TrimStart('/'))
            .ToList();

        routes.Should().Contain(["/health/live", "/health/ready", "/health", "/alive"]);
        routes.Any(r => r.StartsWith("/health-ui", StringComparison.Ordinal)).Should().Be(enabled);
    }

    [Fact]
    public async Task readiness_includes_checks_added_by_other_components_while_liveness_stays_self_only()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration["HealthOptions:DependencyChecksEnabled"] = "false";
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddCustomHealthCheck();
        builder.Services.AddHealthChecks().AddCheck("untagged-store", () => HealthCheckResult.Unhealthy("down"));

        await using var app = builder.Build();
        app.UseCustomHealthCheck();
        await app.StartAsync();
        var client = app.GetTestClient();

        foreach (var livePath in new[] { "/health/live", "/alive" })
        {
            var live = await client.GetAsync(livePath);
            live.StatusCode.Should().Be(HttpStatusCode.OK, livePath);
            (await live.Content.ReadAsStringAsync()).Should().Be("Healthy");
        }

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await ready.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"Unhealthy\"").And.Contain("\"untagged-store\"").And.Contain("\"self\"");

        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    private static ServiceCollection BuildServices(Dictionary<string, string?> settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddCustomHealthCheck();
        return services;
    }
}
