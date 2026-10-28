using Booking.Extensions.Infrastructure;
using Booking.Host.Extensions;
using BuildingBlocks.EventStoreDB.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Host.Test;

public class EventStoreDBSubscriptionHealthCheckTests
{
    private const string CheckName = "eventstore-subscription";

    [Fact]
    public async Task IsHealthyWhenSubscriptionIsActive()
    {
        var state = new EventStoreDBSubscriptionState();
        state.MarkSubscribed();

        var result = await CheckAsync(state);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task IsUnhealthyBeforeSubscriptionStarts()
    {
        var result = await CheckAsync(new EventStoreDBSubscriptionState());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("EventStoreDB $all subscription is not active", result.Description);
    }

    [Fact]
    public async Task BecomesUnhealthyAgainAfterSubscriptionDrops()
    {
        var state = new EventStoreDBSubscriptionState();
        state.MarkSubscribed();
        state.MarkUnsubscribed();

        var result = await CheckAsync(state);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task IsUnhealthyWhenStateIsNotRegistered()
    {
        await using var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var healthCheck = new EventStoreDBSubscriptionHealthCheck(serviceProvider);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task ComposedHostGatesReadinessOnSubscriptionByDefault()
    {
        var builder = CreateBuilder();
        builder.AddBookingHostInfrastructure();
        builder.AddBookingModules();

        await using var serviceProvider = builder.Services.BuildServiceProvider();
        var registration = Assert.Single(Registrations(serviceProvider), r => r.Name == CheckName);
        Assert.Equal(new[] { "ready" }, registration.Tags);

        var state = serviceProvider.GetRequiredService<EventStoreDBSubscriptionState>();
        Assert.False(state.IsSubscribed);
        var check = registration.Factory(serviceProvider);
        Assert.Equal(
            HealthStatus.Unhealthy,
            (await check.CheckHealthAsync(new HealthCheckContext { Registration = registration })).Status
        );

        state.MarkSubscribed();
        Assert.Equal(
            HealthStatus.Healthy,
            (await check.CheckHealthAsync(new HealthCheckContext { Registration = registration })).Status
        );
    }

    [Fact]
    public async Task ComposedHostSkipsSubscriptionGateWhenBackgroundProcessingIsDisabled()
    {
        var builder = CreateBuilder(("Modules:Booking:BackgroundProcessingEnabled", "false"));
        builder.AddBookingHostInfrastructure();
        builder.AddBookingModules();

        await using var serviceProvider = builder.Services.BuildServiceProvider();

        Assert.DoesNotContain(Registrations(serviceProvider), r => r.Name == CheckName);
        Assert.Contains(Registrations(serviceProvider), r => r.Name == "eventstore");
        Assert.Null(serviceProvider.GetService<EventStoreDBSubscriptionState>());
    }

    [Fact]
    public void DockerfileEnvironmentMatchesDockerAppSettingsFileName()
    {
        var hostDirectory = Path.Combine(FindRepositoryRoot(), "src", "Services", "Booking.Host");
        var environment = File.ReadAllLines(Path.Combine(hostDirectory, "Dockerfile"))
            .Select(line => line.Trim())
            .Single(line => line.StartsWith("ENV ASPNETCORE_ENVIRONMENT=", StringComparison.Ordinal))
            .Split('=', 2)[1];

        var settingsFiles = Directory.GetFiles(hostDirectory, "appsettings.*.json").Select(Path.GetFileName).ToArray();

        Assert.Contains($"appsettings.{environment}.json", settingsFiles, StringComparer.Ordinal);
    }

    private static async Task<HealthCheckResult> CheckAsync(EventStoreDBSubscriptionState state)
    {
        await using var serviceProvider = new ServiceCollection().AddSingleton(state).BuildServiceProvider();
        var healthCheck = new EventStoreDBSubscriptionHealthCheck(serviceProvider);
        return await healthCheck.CheckHealthAsync(new HealthCheckContext());
    }

    private static IReadOnlyCollection<HealthCheckRegistration> Registrations(IServiceProvider serviceProvider) =>
        serviceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations.ToArray();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return directory.FullName;
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
}
