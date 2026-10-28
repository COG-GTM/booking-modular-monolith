using System.Net;
using Booking.Extensions.Infrastructure;
using Booking.Host.Extensions;
using BuildingBlocks.MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;
using TransportType = BuildingBlocks.MassTransit.TransportType;

namespace Host.Test;

public class BookingHostCompositionTests
{
    [Fact]
    public async Task HostRegistersExpectedHealthChecksAndJwtMetadataAddress()
    {
        var builder = CreateBuilder();
        builder.AddBookingHostInfrastructure();
        builder.AddBookingModules();

        await using var serviceProvider = builder.Services.BuildServiceProvider();
        var registrations = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        AssertTags(registrations, "self", "live");
        foreach (
            var name in new[]
            {
                "postgres-booking-persist-message",
                "mongo-booking",
                "eventstore",
                "grpc-flight",
                "grpc-passenger",
                "rabbitmq",
            }
        )
            AssertTags(registrations, name, "ready");

        var jwtOptions = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal("http://localhost:5101/.well-known/openid-configuration", jwtOptions.MetadataAddress);
    }

    [Fact]
    public async Task EmptyJwtMetadataOverrideLeavesAuthorityMetadataAddressInPlace()
    {
        var builder = CreateBuilder(("Jwt:MetadataAddress", ""));
        builder.AddBookingHostInfrastructure();
        builder.AddBookingModules();

        await using var serviceProvider = builder.Services.BuildServiceProvider();
        var jwtOptions = serviceProvider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.EndsWith("/.well-known/openid-configuration", jwtOptions.MetadataAddress, StringComparison.Ordinal);
        Assert.StartsWith(jwtOptions.Authority, jwtOptions.MetadataAddress, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RabbitMqHealthCheckIsOmittedWhenTransportIsInMemory()
    {
        var builder = CreateBuilder(("Messaging:Transport", TransportType.InMemory.ToString()));
        builder.AddBookingHostInfrastructure();
        builder.AddBookingModules();

        await using var serviceProvider = builder.Services.BuildServiceProvider();
        var registrations = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        Assert.DoesNotContain(registrations, registration => registration.Name == "rabbitmq");
    }

    [Fact]
    public async Task PipelineServesRootAndLiveHealthWhileUnavailableStoresFailReadiness()
    {
        var builder = CreateBuilder(
            ("Messaging:Transport", TransportType.InMemory.ToString()),
            ("Modules:Booking:BackgroundProcessingEnabled", "false"),
            (
                "PersistMessageOptions:ConnectionString:Booking",
                "Host=127.0.0.1;Port=1;Database=booking_test;Username=test;Password=test;Timeout=2"
            ),
            (
                "PostgresOptions:ConnectionString:Booking",
                "Host=127.0.0.1;Port=1;Database=booking_test;Username=test;Password=test;Timeout=2"
            ),
            (
                "MongoOptions:ConnectionString",
                "mongodb://127.0.0.1:1/?serverSelectionTimeoutMS=500&connectTimeoutMS=500"
            ),
            ("EventStoreOptions:ConnectionString", "esdb://127.0.0.1:1?tls=false"),
            ("Grpc:FlightAddress", "http://127.0.0.1:1"),
            ("Grpc:PassengerAddress", "http://127.0.0.1:1")
        );
        builder.WebHost.UseTestServer();
        builder.AddBookingHostInfrastructure();
        builder.AddBookingModules();

        await using var app = builder.Build();
        app.UseBookingHostInfrastructure();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var rootResponse = await client.GetAsync("/", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, rootResponse.StatusCode);
        Assert.Equal("Booking-Service", await rootResponse.Content.ReadAsStringAsync(timeout.Token));

        using var liveResponse = await client.GetAsync("/health/live", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        var readinessTask = client.GetAsync("/health/ready", timeout.Token);
        var completed = await Task.WhenAny(readinessTask, Task.Delay(TimeSpan.FromSeconds(20), timeout.Token));
        Assert.Same(readinessTask, completed);
        using var readinessResponse = await readinessTask;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readinessResponse.StatusCode);
    }

    private static WebApplicationBuilder CreateBuilder(params (string Key, string Value)[] overrides)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "test",
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = "Booking.Host",
            }
        );
        if (overrides.Length > 0)
            builder.Configuration.AddInMemoryCollection(
                overrides.ToDictionary(setting => setting.Key, setting => (string?)setting.Value)
            );
        return builder;
    }

    private static void AssertTags(IEnumerable<HealthCheckRegistration> registrations, string name, string expectedTag)
    {
        var registration = Assert.Single(registrations, candidate => candidate.Name == name);
        Assert.Equal(new[] { expectedTag }, registration.Tags);
    }
}
