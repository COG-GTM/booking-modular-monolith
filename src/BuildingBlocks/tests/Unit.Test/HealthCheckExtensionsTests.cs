using System.Net;
using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class HealthCheckExtensionsTests
{
    private static IReadOnlyList<HealthCheckRegistration> Registrations(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddCustomHealthCheck();
        services.AddCustomHealthCheck();

        return services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToList();
    }

    [Fact]
    public void registers_self_as_the_only_liveness_check_and_is_idempotent()
    {
        var registrations = Registrations(new());

        registrations.Should().ContainSingle();
        registrations[0].Name.Should().Be(Extensions.SelfCheckName);
        registrations[0].Tags.Should().Contain(Extensions.LiveTag);
    }

    [Fact]
    public void registers_readiness_checks_for_every_configured_dependency()
    {
        var registrations = Registrations(
            new()
            {
                ["PostgresOptions:ConnectionString:Flight"] = "Server=db;Database=flight_service",
                ["PersistMessageOptions:ConnectionString:Flight"] = "Server=db;Database=flight_service_persist_message",
                ["MongoOptions:ConnectionString"] = "mongodb://mongo:27017",
                ["EventStoreOptions:ConnectionString"] = "esdb://eventstore:2113?tls=false",
                ["MessageBroker:TransportType"] = "RabbitMq",
            }
        );

        registrations
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo(
                "self",
                "postgres-flight",
                "postgres-flight-persist-message",
                "mongodb",
                "eventstore",
                "rabbitmq"
            );
        registrations.Where(r => r.Name != "self").Should().OnlyContain(r => r.Tags.Contains(Extensions.ReadyTag));
        registrations.Where(r => r.Name != "self").Should().OnlyContain(r => !r.Tags.Contains(Extensions.LiveTag));
    }

    [Fact]
    public void single_postgres_connection_string_is_supported()
    {
        var registrations = Registrations(new() { ["PostgresOptions:ConnectionString"] = "Server=db;Database=x" });

        registrations.Select(r => r.Name).Should().BeEquivalentTo("self", "postgres");
    }

    [Fact]
    public void dependency_checks_can_be_disabled()
    {
        var registrations = Registrations(
            new()
            {
                ["HealthOptions:DependencyChecksEnabled"] = "false",
                ["MongoOptions:ConnectionString"] = "mongodb://mongo:27017",
            }
        );

        registrations.Select(r => r.Name).Should().BeEquivalentTo("self");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task liveness_and_readiness_probes_are_mapped_in_every_environment(string environment)
    {
        await using var app = await StartAppAsync(environment, failingDependency: false);
        var client = app.GetTestClient();

        foreach (var path in new[] { "/health/live", "/health/ready", "/health", "/alive" })
        {
            var response = await client.GetAsync(path);
            response.StatusCode.Should().Be(HttpStatusCode.OK, path);
        }
    }

    [Fact]
    public async Task failing_dependency_fails_readiness_but_not_liveness()
    {
        await using var app = await StartAppAsync("Production", failingDependency: true);
        var client = app.GetTestClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ready.Content.ReadAsStringAsync()).Should().Contain("grpc-flight");
    }

    private static async Task<WebApplication> StartAppAsync(string environment, bool failingDependency)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddCustomHealthCheck();

        if (failingDependency)
        {
            builder.Services.AddHealthChecks()
                .AddCheck("grpc-flight", () => HealthCheckResult.Unhealthy("down"), [Extensions.ReadyTag]);
        }

        var app = builder.Build();
        app.UseCustomHealthCheck();
        await app.StartAsync();

        return app;
    }
}
