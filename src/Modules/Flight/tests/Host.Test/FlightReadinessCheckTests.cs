using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Mongo;
using Flight.Host.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Xunit;

namespace Host.Test;

public class FlightReadinessCheckTests
{
    [Fact]
    public async Task postgres_check_is_unhealthy_when_connection_is_not_configured()
    {
        var check = new FlightPostgresReadinessCheck(Configuration());

        var result = await check.CheckHealthAsync(CreatePostgresContext("flight-postgres", check));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Flight module PostgreSQL connection is not configured.");
    }

    [Theory]
    [InlineData("PostgresOptions:ConnectionString:Flight")]
    [InlineData("ConnectionStrings:flight")]
    public async Task postgres_check_reports_unavailable_for_configured_unreachable_connection(string key)
    {
        var configuration = Configuration((key, UnreachablePostgresConnectionString()));
        var check = new FlightPostgresReadinessCheck(configuration);

        var result = await check.CheckHealthAsync(CreatePostgresContext("flight-postgres", check));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Flight module PostgreSQL is unavailable.");
    }

    [Fact]
    public async Task outbox_postgres_check_is_unhealthy_when_connection_is_not_configured()
    {
        var check = new FlightPostgresReadinessCheck(Configuration());

        var result = await check.CheckHealthAsync(CreatePostgresContext("flight-outbox-postgres", check));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Flight outbox PostgreSQL connection is not configured.");
    }

    [Fact]
    public async Task outbox_postgres_check_skips_probe_when_it_shares_the_module_connection()
    {
        var check = new FlightPostgresReadinessCheck(
            Configuration(("PostgresOptions:ConnectionString:Flight", UnreachablePostgresConnectionString()))
        );

        var result = await check.CheckHealthAsync(CreatePostgresContext("flight-outbox-postgres", check));

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("Flight outbox shares the Flight module PostgreSQL database.");
    }

    [Theory]
    [InlineData("ConnectionStrings:flight-persist-message")]
    [InlineData("PersistMessageOptions:ConnectionString:Flight")]
    public async Task outbox_postgres_check_reports_unavailable_for_an_unreachable_override(string key)
    {
        var moduleConnectionString = UnreachablePostgresConnectionString();
        var outboxConnectionString = UnreachablePostgresConnectionString();
        outboxConnectionString.Should().NotBe(moduleConnectionString);
        var check = new FlightPostgresReadinessCheck(
            Configuration(
                ("PostgresOptions:ConnectionString:Flight", moduleConnectionString),
                (key, outboxConnectionString)
            )
        );

        var result = await check.CheckHealthAsync(CreatePostgresContext("flight-outbox-postgres", check));

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Flight outbox PostgreSQL is unavailable.");
    }

    [Theory]
    [InlineData("InMemory")]
    [InlineData("inmemory")]
    public async Task rabbitmq_check_is_healthy_for_in_memory_transport(string transport)
    {
        var check = new RabbitMqReadinessCheck(Configuration(("MessagingOptions:Transport", transport)));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Be("RabbitMQ check skipped for the InMemory transport.");
    }

    [Theory]
    [InlineData("RabbitMq")]
    [InlineData(null)]
    public async Task rabbitmq_check_is_unhealthy_for_unreachable_rabbitmq(string? transport)
    {
        var configuration = Configuration(
            ("MessagingOptions:Transport", transport),
            ("RabbitMqOptions:HostName", "127.0.0.1"),
            ("RabbitMqOptions:Port", UnusedPort().ToString())
        );
        var check = new RabbitMqReadinessCheck(configuration);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("RabbitMQ is unavailable.");
    }

    [Fact]
    public async Task mongo_check_reports_unavailable_for_unreachable_mongodb()
    {
        using var provider = CreateMongoOptionsProvider(UnreachableMongoConnectionString());
        var check = new FlightMongoReadinessCheck(provider.GetRequiredService<IOptionsMonitor<MongoOptions>>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Flight MongoDB is unavailable.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task mongo_check_reports_not_configured_for_an_empty_connection_string(string connectionString)
    {
        using var provider = CreateMongoOptionsProvider(connectionString);
        var check = new FlightMongoReadinessCheck(provider.GetRequiredService<IOptionsMonitor<MongoOptions>>());

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Flight MongoDB connection is not configured.");
    }

    [Fact]
    public void readiness_check_registration_has_expected_names_and_does_not_register_a_mongo_client()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(Configuration());

        services.AddFlightReadinessChecks();

        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var expectedNames = new[] { "flight-postgres", "flight-outbox-postgres", "flight-mongo", "rabbitmq" };

        registrations.Select(registration => registration.Name).Should().Contain(expectedNames);
        foreach (var registration in registrations.Where(registration => expectedNames.Contains(registration.Name)))
        {
            registration.Tags.Should().BeEquivalentTo(["ready"]);
            registration.Tags.Should().NotContain("live");
            registration.Timeout.Should().Be(TimeSpan.FromSeconds(5));
            registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        }

        provider.GetService<IMongoClient>().Should().BeNull();
    }

    [Fact]
    public async Task readiness_endpoint_fails_for_unavailable_dependencies_while_liveness_succeeds()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = UnreachablePostgresConnectionString(),
                ["MongoOptions:ConnectionString"] = UnreachableMongoConnectionString(),
                ["MongoOptions:Flight:DatabaseName"] = "flight_service_read",
                ["MessagingOptions:Transport"] = "InMemory",
            }
        );
        builder
            .Services.AddOptions<MongoOptions>("Flight")
            .Configure<IConfiguration>(
                (options, configuration) =>
                {
                    options.DatabaseName = configuration["MongoOptions:Flight:DatabaseName"]!;
                    options.ConnectionString = configuration["MongoOptions:ConnectionString"]!;
                }
            );
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        builder.Services.AddFlightReadinessChecks();

        await using var app = builder.Build();
        app.MapFlightHealthEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var ready = await client.GetAsync("/health/ready");
        using var live = await client.GetAsync("/health/live");

        ready.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        live.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(value => value.Key, value => value.Value))
            .Build();

    private static HealthCheckContext CreatePostgresContext(string name, FlightPostgresReadinessCheck check) =>
        new()
        {
            Registration = new HealthCheckRegistration(
                name,
                check,
                HealthStatus.Unhealthy,
                ["ready"],
                TimeSpan.FromSeconds(5)
            ),
        };

    private static ServiceProvider CreateMongoOptionsProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services
            .AddOptions<MongoOptions>("Flight")
            .Configure(options =>
            {
                options.DatabaseName = "flight_service_read";
                options.ConnectionString = connectionString;
            });
        return services.BuildServiceProvider();
    }

    private static int UnusedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string UnreachablePostgresConnectionString() =>
        $"Host=127.0.0.1;Port={UnusedPort()};Database=flight_service;Username=flight_service;Password=flight_service;Timeout=2";

    private static string UnreachableMongoConnectionString() =>
        $"mongodb://127.0.0.1:{UnusedPort()}/?serverSelectionTimeoutMS=500&connectTimeoutMS=500";
}
