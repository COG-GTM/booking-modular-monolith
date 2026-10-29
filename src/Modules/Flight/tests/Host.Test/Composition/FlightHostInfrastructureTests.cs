using System.Net;
using System.Text.Json;
using BuildingBlocks.EFCore;
using BuildingBlocks.PersistMessageProcessor;
using Flight.Host.Extensions;
using Grpc.Health.V1;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Flight.Host.Test.Composition;

public class FlightHostInfrastructureTests
{
    [Theory]
    [InlineData(
        "Production",
        "localhost",
        "http://localhost:5101",
        "http://localhost:5101/.well-known/openid-configuration"
    )]
    [InlineData(
        "docker",
        "postgres",
        "http://localhost:5000",
        "http://identity-service:8080/.well-known/openid-configuration"
    )]
    public async Task host_configuration_keeps_flight_stores_and_jwt_discovery_separate(
        string environment,
        string expectedDatabaseHost,
        string expectedAuthority,
        string expectedMetadataAddress
    )
    {
        var builder = CreateBuilder(environment);
        builder.AddFlightHostInfrastructure();
        await using var provider = builder.Services.BuildServiceProvider();
        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var write = new NpgsqlConnectionStringBuilder(builder.Configuration.ResolvePostgresConnectionString("Flight"));
        var outbox = new NpgsqlConnectionStringBuilder(
            builder.Configuration.ResolvePersistMessageConnectionString("Flight")
        );

        Assert.Equal(expectedDatabaseHost, write.Host);
        Assert.Equal(expectedDatabaseHost, outbox.Host);
        Assert.Equal("flight_service", write.Database);
        Assert.Equal("flight_service_persist_message", outbox.Database);
        Assert.Equal("flight_service", write.Username);
        Assert.Equal("flight_service", outbox.Username);
        Assert.Equal("flight_service_read", builder.Configuration["MongoOptions:Flight:DatabaseName"]);
        Assert.Equal("flight-service", builder.Configuration["ObservabilityOptions:ServiceName"]);
        Assert.Equal(expectedAuthority, jwt.Authority);
        Assert.Equal("booking-modular-monolith", jwt.Audience);
        Assert.Equal(expectedMetadataAddress, jwt.MetadataAddress);
        Assert.Equal([jwt.Authority], jwt.TokenValidationParameters.ValidIssuers);
        Assert.Equal([jwt.Audience], jwt.TokenValidationParameters.ValidAudiences);
    }

    [Fact]
    public async Task metadata_override_does_not_change_the_public_token_issuer()
    {
        var builder = CreateBuilder(
            "Production",
            new KeyValuePair<string, string?>(
                "Jwt:MetadataAddress",
                "http://custom-identity/.well-known/openid-configuration"
            )
        );
        builder.AddFlightHostInfrastructure();
        await using var provider = builder.Services.BuildServiceProvider();

        var jwt = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal("http://custom-identity/.well-known/openid-configuration", jwt.MetadataAddress);
        Assert.Equal("http://localhost:5101", jwt.Authority);
        Assert.Equal([jwt.Authority], jwt.TokenValidationParameters.ValidIssuers);
    }

    [Theory]
    [InlineData("Production", null, true)]
    [InlineData("test", null, false)]
    [InlineData("test", "RabbitMq", true)]
    [InlineData("test", "inmemory", false)]
    [InlineData("Development", "InMemory", false)]
    public async Task readiness_registers_rabbitmq_only_for_the_broker_transport(
        string environment,
        string? transport,
        bool expectsRabbitMq
    )
    {
        var builder = CreateBuilder(
            environment,
            new("Messaging:Transport", transport),
            new("Messaging:AllowInMemory", "true")
        );
        builder.AddFlightHostInfrastructure();
        await using var provider = builder.Services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;

        Assert.Equal(expectsRabbitMq, registrations.Any(check => check.Name == "rabbitmq"));
        var self = Assert.Single(registrations, check => check.Name == "self");
        Assert.Contains("live", self.Tags);
        Assert.DoesNotContain("ready", self.Tags);
        var names = new List<string> { "postgres-flight", "postgres-flight-persist-message", "mongo-flight" };
        if (expectsRabbitMq)
            names.Add("rabbitmq");
        foreach (var name in names)
        {
            var check = Assert.Single(registrations, check => check.Name == name);
            Assert.Contains("ready", check.Tags);
            Assert.DoesNotContain("live", check.Tags);
            Assert.Equal(HealthStatus.Unhealthy, check.FailureStatus);
        }
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, HttpStatusCode.OK)]
    [InlineData(HealthStatus.Unhealthy, HttpStatusCode.ServiceUnavailable)]
    public async Task http_probes_separate_liveness_from_dependency_readiness(
        HealthStatus dependencyStatus,
        HttpStatusCode expectedStatus
    )
    {
        await using var app = await StartProbeHostAsync(dependencyStatus);
        using var client = app.GetTestClient();

        using var live = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("application/json", live.Content.Headers.ContentType?.MediaType);
        using var liveJson = JsonDocument.Parse(await live.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", liveJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "self",
            Assert.Single(liveJson.RootElement.GetProperty("checks").EnumerateArray()).GetProperty("name").GetString()
        );

        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(expectedStatus, ready.StatusCode);
        Assert.Equal("application/json", ready.Content.Headers.ContentType?.MediaType);
        using var readyJson = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Equal(dependencyStatus.ToString(), readyJson.RootElement.GetProperty("status").GetString());
        var checks = readyJson
            .RootElement.GetProperty("checks")
            .EnumerateArray()
            .ToDictionary(check => check.GetProperty("name").GetString()!);
        Assert.Equal(
            ["mongo-flight", "postgres-flight", "postgres-flight-persist-message", "self"],
            checks.Keys.Order()
        );
        Assert.Equal("Healthy", checks["self"].GetProperty("status").GetString());
        foreach (var name in new[] { "postgres-flight", "postgres-flight-persist-message", "mongo-flight" })
            Assert.Equal(dependencyStatus.ToString(), checks[name].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, HealthCheckResponse.Types.ServingStatus.Serving)]
    [InlineData(HealthStatus.Unhealthy, HealthCheckResponse.Types.ServingStatus.NotServing)]
    public async Task grpc_health_maps_the_empty_service_to_live_and_flight_to_ready(
        HealthStatus dependencyStatus,
        HealthCheckResponse.Types.ServingStatus expectedReadyStatus
    )
    {
        await using var app = await StartProbeHostAsync(dependencyStatus);
        using var channel = GrpcChannel.ForAddress(
            "http://localhost",
            new GrpcChannelOptions { HttpHandler = app.GetTestServer().CreateHandler() }
        );
        var client = new global::Grpc.Health.V1.Health.HealthClient(channel);

        var live = await client.CheckAsync(new HealthCheckRequest { Service = "" });
        var ready = await client.CheckAsync(new HealthCheckRequest { Service = "flight.FlightGrpcService" });

        Assert.Equal(HealthCheckResponse.Types.ServingStatus.Serving, live.Status);
        Assert.Equal(expectedReadyStatus, ready.Status);
    }

    private static WebApplicationBuilder CreateBuilder(
        string environment,
        params KeyValuePair<string, string?>[] overrides
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = environment,
                ApplicationName = typeof(global::Flight.Host.Program).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
            }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
            }
        );
        builder.Configuration.AddInMemoryCollection(overrides);
        return builder;
    }

    private static async Task<WebApplication> StartProbeHostAsync(HealthStatus dependencyStatus)
    {
        var builder = CreateBuilder("test", new KeyValuePair<string, string?>("Messaging:Transport", "InMemory"));
        builder.WebHost.UseTestServer();
        builder.AddFlightHostInfrastructure();
        builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            foreach (var check in options.Registrations.Where(check => check.Name != "self"))
                check.Factory = _ => new StubHealthCheck(
                    check.Name == "masstransit-bus" ? HealthStatus.Unhealthy : dependencyStatus
                );
            options.Registrations.Add(
                new HealthCheckRegistration(
                    "unrelated",
                    _ => new StubHealthCheck(HealthStatus.Unhealthy),
                    HealthStatus.Unhealthy,
                    []
                )
            );
        });
        var app = builder.Build();
        app.UseFlightHostInfrastructure();
        await app.StartAsync();
        return app;
    }

    private sealed class StubHealthCheck(HealthStatus status) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new HealthCheckResult(status));
    }
}
