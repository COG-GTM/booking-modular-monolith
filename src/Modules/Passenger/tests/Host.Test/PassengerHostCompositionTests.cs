using System.Net;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Passenger.Extensions.Infrastructure;
using Passenger.Host.Extensions;
using Passenger.Host.Health;
using Xunit;

namespace Passenger.Host.Test;

public sealed class PassengerHostCompositionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task standalone_host_keeps_its_consumer_when_monolith_processing_is_disabled(bool enabled)
    {
        var builder = Builder("test");
        builder.Configuration["Modules:Passenger:BackgroundProcessingEnabled"] = enabled.ToString();
        builder.AddPassengerHostInfrastructure();
        builder.AddPassengerModules();
        await using var app = builder.Build();

        app.Services.GetServices<IConsumerRegistration>().Should().ContainSingle();
        var workers = app
            .Services.GetServices<IHostedService>()
            .OfType<PersistMessageBackgroundService<global::Passenger.PassengerRoot>>();
        workers.Count().Should().Be(enabled ? 1 : 0);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("RabbitMq", true)]
    [InlineData("InMemory", false)]
    public async Task readiness_registers_only_the_configured_transport(string? transport, bool rabbitMq)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MessagingOptions:Transport"] = transport })
            .Build();
        services.AddLogging();
        services.AddPassengerHealthChecks(configuration);
        await using var provider = services.BuildServiceProvider();

        var names = provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(registration => registration.Name);
        names
            .Should()
            .BeEquivalentTo(
                rabbitMq
                    ? new[] { "passenger-db", "passenger-read-db", "rabbitmq" }
                    : new[] { "passenger-db", "passenger-read-db" }
            );
    }

    [Fact]
    public void unsupported_transport_fails_during_health_registration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MessagingOptions:Transport"] = "unsupported" })
            .Build();

        var register = () => new ServiceCollection().AddPassengerHealthChecks(configuration);

        register.Should().Throw<InvalidOperationException>().WithMessage("*Unsupported MessagingOptions:Transport*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    [InlineData("test")]
    public async Task liveness_stays_healthy_when_a_readiness_dependency_fails(string environment)
    {
        var builder = Builder(environment);
        builder.Configuration["PersistMessageOptions:Enabled"] = "false";
        builder.WebHost.UseTestServer();
        builder.AddPassengerHostInfrastructure();
        builder.AddPassengerModules();
        builder.Services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            options.Registrations.Clear();
            options.Registrations.Add(
                new HealthCheckRegistration(
                    "process",
                    _ => new StubHealthCheck(HealthCheckResult.Healthy()),
                    null,
                    new[] { "live" }
                )
            );
            options.Registrations.Add(
                new HealthCheckRegistration(
                    "dependency",
                    _ => new StubHealthCheck(HealthCheckResult.Unhealthy()),
                    null,
                    Array.Empty<string>()
                )
            );
        });
        await using var app = builder.Build();
        app.UsePassengerHostInfrastructure();
        await app.StartAsync();
        using var client = app.GetTestClient();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await client.GetStringAsync("/")).Should().Be("Passenger-Service");
    }

    [Fact]
    public async Task passenger_named_mongo_options_override_shared_database_and_connection()
    {
        var builder = Builder("test");
        builder.Configuration["MongoOptions:DatabaseName"] = "monolith";
        builder.Configuration["MongoOptions:Passenger:ConnectionString"] = "mongodb://passenger-store:27017";
        builder.AddPassengerHostInfrastructure();
        builder.AddPassengerModules();
        await using var app = builder.Build();

        var options = app.Services.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Passenger");
        options.DatabaseName.Should().Be("passenger_service_read");
        options.ConnectionString.Should().Be("mongodb://passenger-store:27017");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task missing_passenger_mongo_database_is_rejected(string database)
    {
        var builder = Builder("test");
        builder.Configuration["MongoOptions:Passenger:DatabaseName"] = database;
        builder.AddPassengerHostInfrastructure();
        builder.AddPassengerModules();
        await using var app = builder.Build();

        var resolve = () => app.Services.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Passenger");

        resolve
            .Should()
            .Throw<OptionsValidationException>()
            .WithMessage("*MongoOptions:Passenger:DatabaseName is required*");
    }

    private static WebApplicationBuilder Builder(string environment)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration["MessagingOptions:Transport"] = "InMemory";
        return builder;
    }

    private sealed class StubHealthCheck(HealthCheckResult result) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(result);
    }
}
