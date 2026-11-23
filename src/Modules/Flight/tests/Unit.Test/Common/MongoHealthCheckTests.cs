using BuildingBlocks.HealthCheck;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Common;

public class MongoHealthCheckTests
{
    [Theory]
    [InlineData("mongodb://localhost:27017/?tls=true", null)]
    [InlineData("mongodb://localhost:27017/?tls=true", "invalid")]
    [InlineData(null, "mongodb://localhost:27017")]
    public void Mongo_health_check_uses_aspire_connection_string_when_available(
        string? aspireConnectionString,
        string? fallbackConnectionString
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["HealthOptions:Enabled"] = "true",
                    ["ConnectionStrings:mongo"] = aspireConnectionString,
                    ["MongoOptions:ConnectionString"] = fallbackConnectionString,
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCustomHealthCheck(configuration);
        using var serviceProvider = services.BuildServiceProvider();
        var registrations = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
        var registration = Assert.Single(registrations, item => item.Name == "MongoDB-Health");

        Assert.NotNull(registration.Factory(serviceProvider));
    }
}
