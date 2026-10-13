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

public class EventStoreDbHealthCheckTests
{
    [Theory]
    [InlineData("esdb://localhost:2113?tls=false", "http://localhost:2113/health/live")]
    [InlineData("esdb://admin:changeit@node1:2113,node2:2113?tls=true", "https://node1:2113/health/live")]
    [InlineData("esdb+discover://cluster.local:2113", "https://cluster.local:2113/health/live")]
    public void builds_the_liveness_uri_from_a_grpc_connection_string(string connectionString, string expected)
    {
        EventStoreDbHealthCheck.TryGetLivenessUri(connectionString, out var uri).Should().BeTrue();
        uri.ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a connection string")]
    public void rejects_invalid_connection_strings(string? connectionString) =>
        EventStoreDbHealthCheck.TryGetLivenessUri(connectionString, out _).Should().BeFalse();
}

public class HealthCheckConnectionResolutionTests
{
    [Fact]
    public void aspire_connection_strings_win_over_options_and_register_aspire_only_stores()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["PostgresOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight_service",
                    ["ConnectionStrings:flight"] = "Host=postgres;Database=flight_service",
                    ["ConnectionStrings:flight-persist-message"] = "Host=postgres;Database=flight_service_persist_message",
                    ["ConnectionStrings:mongo"] = "mongodb://mongo:27017",
                    ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                }
            )
            .Build();

        var connections = BuildingBlocks.HealthCheck.Extensions
            .GetPostgresConnections(configuration)
            .ToDictionary(c => c.Name, c => c.Resolve(configuration));

        connections.Should().BeEquivalentTo(
            new Dictionary<string, string?>
            {
                ["postgres-flight"] = "Host=postgres;Database=flight_service",
                ["postgres-flight-persist-message"] = "Host=postgres;Database=flight_service_persist_message",
            }
        );
        BuildingBlocks.HealthCheck.Extensions.GetMongoConnectionString(configuration).Should().Be("mongodb://mongo:27017");
    }

    [Fact]
    public void multiword_outbox_modules_resolve_their_options_key_with_the_original_spelling()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["PersistMessageOptions:ConnectionString:FlightSchedule"] = "Host=db" }
            )
            .Build();

        var connections = BuildingBlocks.HealthCheck.Extensions
            .GetPostgresConnections(configuration)
            .ToDictionary(c => c.Name, c => c.Resolve(configuration));

        connections.Should().BeEquivalentTo(
            new Dictionary<string, string?> { ["postgres-flight-schedule-persist-message"] = "Host=db" }
        );
    }
}
