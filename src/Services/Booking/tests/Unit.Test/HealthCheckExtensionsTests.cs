using Booking.Host.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Booking.Host.Unit.Test;

public class HealthCheckExtensionsTests
{
    private static readonly string[] OwnedStoreCheckNames =
    [
        HealthCheckExtensions.PostgresCheckName,
        HealthCheckExtensions.MongoCheckName,
        HealthCheckExtensions.EventStoreCheckName,
        HealthCheckExtensions.RabbitMqCheckName,
    ];

    private static WebApplication BuildApp(params KeyValuePair<string, string?>[] overrides)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = "test",
            }
        );
        builder.Configuration.AddInMemoryCollection(overrides);

        builder.AddBookingHost();

        return builder.Build();
    }

    private static IReadOnlyDictionary<string, HealthCheckRegistration> GetRegistrations(WebApplication app) =>
        app
            .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToDictionary(r => r.Name);

    [Fact]
    public void tags_every_owned_store_check_as_readiness()
    {
        using var app = BuildApp();
        var registrations = GetRegistrations(app);

        foreach (var name in OwnedStoreCheckNames)
        {
            registrations
                .Should()
                .ContainKey(name)
                .WhoseValue.Tags.Should()
                .Contain(BuildingBlocks.Grpc.Extensions.ReadinessTag, $"{name} must gate readiness");
        }
    }

    [Fact]
    public void does_not_tag_owned_store_checks_as_grpc_dependencies()
    {
        using var app = BuildApp();
        var registrations = GetRegistrations(app);

        foreach (var name in OwnedStoreCheckNames)
        {
            registrations[name].Tags.Should().NotContain(BuildingBlocks.Grpc.Extensions.GrpcDependencyTag);
        }
    }

    [Fact]
    public void eventstore_readiness_uses_grpc_client_based_health_check()
    {
        using var app = BuildApp();
        var registration = GetRegistrations(app)[HealthCheckExtensions.EventStoreCheckName];

        using var scope = app.Services.CreateScope();
        registration.Factory(scope.ServiceProvider).Should().BeOfType<EventStoreHealthCheck>();
        registration.FailureStatus.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void mongo_readiness_fails_as_unhealthy()
    {
        using var app = BuildApp();

        GetRegistrations(app)[HealthCheckExtensions.MongoCheckName].FailureStatus.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void owned_store_checks_are_registered_once()
    {
        using var app = BuildApp();
        var names = app
            .Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(r => r.Name)
            .ToList();

        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void requires_booking_postgres_connection()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = "test",
            }
        );
        builder.Configuration.AddInMemoryCollection(
            [new KeyValuePair<string, string?>("PostgresOptions:ConnectionString:Booking", null)]
        );

        var act = () => builder.AddBookingHost();

        act.Should().Throw<ArgumentException>().WithMessage("*Booking*not found*");
    }
}
