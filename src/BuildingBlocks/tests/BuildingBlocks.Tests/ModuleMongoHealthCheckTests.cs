using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Tests;

public class ModuleMongoHealthCheckTests
{
    [Fact]
    public void mongo_health_checks_are_registered_for_database_configured_modules()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://root:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight",
                ["MongoOptions:Passenger:DatabaseName"] = "passenger",
                ["MongoOptions:Booking:DatabaseName"] = "booking",
                ["MongoOptions:Orphan:ConnectionString"] = "mongodb://orphan:27017",
            }
        );

        MongoRegistrationNames(provider)
            .Should()
            .BeEquivalentTo(["MongoDB-Flight", "MongoDB-Passenger", "MongoDB-Booking"]);
    }

    [Fact]
    public void module_without_connection_and_without_root_or_aspire_connection_has_no_mongo_check()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?> { ["MongoOptions:Flight:DatabaseName"] = "flight" }
        );

        MongoRegistrationNames(provider).Should().BeEmpty();
    }

    [Fact]
    public void module_connection_is_sufficient_without_a_root_connection()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["MongoOptions:Flight:DatabaseName"] = "flight",
                ["MongoOptions:Flight:ConnectionString"] = "mongodb://flight:27017",
            }
        );

        MongoRegistrationNames(provider).Should().ContainSingle().Which.Should().Be("MongoDB-Flight");
    }

    [Fact]
    public void aspire_connection_is_used_for_each_configured_module()
    {
        using var provider = CreateProvider(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:mongo"] = "mongodb://aspire:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight",
                ["MongoOptions:Passenger:DatabaseName"] = "passenger",
            }
        );

        MongoRegistrationNames(provider).Should().BeEquivalentTo(["MongoDB-Flight", "MongoDB-Passenger"]);
    }

    private static ServiceProvider CreateProvider(Dictionary<string, string?> settings)
    {
        settings["HealthOptions:Enabled"] = "true";
        settings["AppOptions:Name"] = "test-app";
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddCustomHealthCheck();
        return services.BuildServiceProvider();
    }

    private static string[] MongoRegistrationNames(IServiceProvider provider) =>
        provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.Select(registration => registration.Name)
            .Where(name => name.StartsWith("MongoDB-", StringComparison.Ordinal))
            .ToArray();
}
