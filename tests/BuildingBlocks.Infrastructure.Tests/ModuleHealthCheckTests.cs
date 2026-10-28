using BuildingBlocks.EFCore;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.PersistMessageProcessor;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class ModuleHealthCheckTests
{
    [Fact]
    public void EnabledHealthChecksCoverWriteAndPersistStoresUsingContextResolution()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["HealthOptions:Enabled"] = "true";
        foreach (var module in new[] { "Flight", "Identity", "Passenger" })
            builder.Configuration[$"PostgresOptions:ConnectionString:{module}"] = $"Host=localhost;Database={module}";
        foreach (var module in new[] { "Flight", "Identity", "Passenger", "Booking" })
            builder.Configuration[$"PersistMessageOptions:ConnectionString:{module}"] =
                $"Host=localhost;Database={module}_persist";
        builder.Configuration["ConnectionStrings:flight"] = "Host=aspire;Database=flight";
        builder.Configuration["PersistMessageOptions:ConnectionString:Booking"] = null;
        builder.Configuration["ConnectionStrings:booking-persist-message"] = "Host=aspire;Database=booking_persist";
        builder.Services.AddCustomHealthCheck();
        builder.AddCustomDbContext<HealthWriteContext>("Flight");
        builder.AddPersistMessageProcessor<HealthModule>("Flight");
        using var provider = builder.Services.BuildServiceProvider();
        var checks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var databaseChecks = checks.Where(x => x.Name.StartsWith("postgres-", StringComparison.Ordinal)).ToList();
        Assert.Equal(7, databaseChecks.Count);
        foreach (
            var name in new[]
            {
                "postgres-flight",
                "postgres-identity",
                "postgres-passenger",
                "postgres-flight-persist-message",
                "postgres-identity-persist-message",
                "postgres-passenger-persist-message",
                "postgres-booking-persist-message",
            }
        )
            Assert.Single(databaseChecks, x => x.Name == name);
        Assert.Equal("Host=aspire;Database=flight", builder.Configuration.ResolvePostgresConnectionString("Flight"));
        Assert.Equal(
            "Host=aspire;Database=booking_persist",
            builder.Configuration.ResolvePersistMessageConnectionString("Booking")
        );
        foreach (var check in databaseChecks)
            Assert.NotNull(check.Factory(provider));
        Assert.Single(checks, x => x.Name == "self");
        Assert.NotNull(provider.GetRequiredService<HealthCheckService>());
    }

    [Fact]
    public void AspireOnlyStoresRegisteredAfterHealthChecksAreCovered()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["HealthOptions:Enabled"] = "true";
        builder.Configuration["Modules:Flight:BackgroundProcessingEnabled"] = "false";
        builder.Services.AddCustomHealthCheck();
        builder.AddCustomDbContext<HealthWriteContext>("Flight");
        builder.AddPersistMessageProcessor<HealthModule>("Flight");
        builder.Configuration["ConnectionStrings:flight"] = "Host=aspire;Database=flight";
        builder.Configuration["ConnectionStrings:flight-persist-message"] = "Host=aspire;Database=flight_persist";
        builder.Configuration["ConnectionStrings:mongo"] = "mongodb://localhost";
        using var provider = builder.Services.BuildServiceProvider();
        var checks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var databaseChecks = checks.Where(x => x.Name.StartsWith("postgres-", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, databaseChecks.Count);
        Assert.Single(databaseChecks, x => x.Name == "postgres-flight");
        Assert.Single(databaseChecks, x => x.Name == "postgres-flight-persist-message");
        foreach (var check in databaseChecks)
            Assert.NotNull(check.Factory(provider));
        using var scope = provider.CreateScope();
        Assert.Equal(
            "Host=aspire;Database=flight",
            scope.ServiceProvider.GetRequiredService<HealthWriteContext>().Database.GetConnectionString()
        );
        Assert.Equal(
            "Host=aspire;Database=flight_persist",
            scope
                .ServiceProvider.GetRequiredService<PersistMessageDbContext<HealthModule>>()
                .Database.GetConnectionString()
        );
    }

    [Fact]
    public void RegisteredStoreWithoutConnectionStringFailsWhenCreatingItsCheck()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["HealthOptions:Enabled"] = "true";
        builder.Services.AddCustomHealthCheck();
        builder.AddCustomDbContext<HealthWriteContext>("Flight");
        using var provider = builder.Services.BuildServiceProvider();
        var checks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        var check = Assert.Single(checks, x => x.Name == "postgres-flight");
        Assert.Throws<ArgumentException>(() => check.Factory(provider));
    }

    [Fact]
    public void EnabledHealthChecksWithoutStoresDoNotRegisterDatabaseChecks()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["HealthOptions:Enabled"] = "true";
        builder.Services.AddCustomHealthCheck();
        using var provider = builder.Services.BuildServiceProvider();
        var checks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.DoesNotContain(checks, x => x.Name.StartsWith("postgres-", StringComparison.Ordinal));
        Assert.Single(checks, x => x.Name == "self");
    }

    [Fact]
    public void DisabledHealthChecksDoNotRegisterDatabaseChecks()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["HealthOptions:Enabled"] = "false";
        builder.Services.AddCustomHealthCheck();
        using var provider = builder.Services.BuildServiceProvider();
        var checks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        Assert.Single(checks, x => x.Name == "self");
        Assert.DoesNotContain(checks, x => x.Name.StartsWith("postgres-", StringComparison.Ordinal));
    }

    private sealed class HealthWriteContext(DbContextOptions<HealthWriteContext> options) : AppDbContextBase(options);

    private sealed class HealthModule;
}
