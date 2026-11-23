using BuildingBlocks.HealthCheck;
using BuildingBlocks.Mongo;
using Flight.Data;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Common;

public class MongoConnectionResolutionTests
{
    [Fact]
    public void Mongo_health_check_is_not_registered_without_any_mongo_connection_string()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?> { ["HealthOptions:Enabled"] = "true" });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddCustomHealthCheck(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var registrations = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
        registrations.Should().NotContain(r => r.Name == "MongoDB-Health");
    }

    [Theory]
    [InlineData("mongodb://aspire:27017/?tls=true", "mongodb://fallback:27017", "mongodb://aspire:27017/?tls=true")]
    [InlineData(null, "mongodb://fallback:27017", "mongodb://fallback:27017")]
    public void AddMongoDbContext_resolves_aspire_connection_string_before_mongo_options(
        string? aspireConnectionString,
        string fallbackConnectionString,
        string expectedConnectionString
    )
    {
        var configuration = BuildConfiguration(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:mongo"] = aspireConnectionString,
                ["MongoOptions:ConnectionString"] = fallbackConnectionString,
                ["MongoOptions:DatabaseName"] = "flight",
            }
        );
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddMongoDbContext<FlightReadDbContext, FlightReadDbContext>(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<IOptions<MongoOptions>>().Value;
        options.ConnectionString.Should().Be(expectedConnectionString);
        options.DatabaseName.Should().Be("flight");
    }

    private static IConfigurationRoot BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
