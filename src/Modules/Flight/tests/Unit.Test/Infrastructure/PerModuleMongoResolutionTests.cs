using BuildingBlocks.Mongo;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Infrastructure;

// AddMongoDbContext<TContext>(builder, moduleName) resolves the server from, in order:
// MongoOptions:<Module>:ConnectionString, ConnectionStrings:<module>-mongo, ConnectionStrings:mongo,
// MongoOptions:ConnectionString. The database name has no shared fallback (AB-492).
public class PerModuleMongoResolutionTests
{
    public sealed class FlightRead(IOptions<MongoOptions> options) : MongoDbContext(options);

    private static FlightRead Resolve(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.AddMongoDbContext<FlightRead>("Flight");

        var provider = builder.Services.BuildServiceProvider();
        return provider.CreateScope().ServiceProvider.GetRequiredService<FlightRead>();
    }

    [Fact]
    public void module_specific_aspire_connection_string_wins_over_shared_aspire_and_options_keys()
    {
        var context = Resolve(
            new()
            {
                ["MongoOptions:Flight:DatabaseName"] = "flight_service_read",
                ["ConnectionStrings:flight-mongo"] = "mongodb://flight-mongo:27017",
                ["ConnectionStrings:mongo"] = "mongodb://shared-aspire-mongo:27017",
                ["MongoOptions:ConnectionString"] = "mongodb://shared-options-mongo:27017",
            }
        );

        Assert.Equal("flight-mongo", context.MongoClient.Settings.Server.Host);
        Assert.Equal("flight_service_read", context.Database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void shared_aspire_connection_string_wins_over_shared_options_key()
    {
        var context = Resolve(
            new()
            {
                ["MongoOptions:Flight:DatabaseName"] = "flight_service_read",
                ["ConnectionStrings:mongo"] = "mongodb://shared-aspire-mongo:27017",
                ["MongoOptions:ConnectionString"] = "mongodb://shared-options-mongo:27017",
            }
        );

        Assert.Equal("shared-aspire-mongo", context.MongoClient.Settings.Server.Host);
    }

    [Fact]
    public void module_options_connection_string_wins_over_every_aspire_key()
    {
        var context = Resolve(
            new()
            {
                ["MongoOptions:Flight:DatabaseName"] = "flight_service_read",
                ["MongoOptions:Flight:ConnectionString"] = "mongodb://flight-options-mongo:27017",
                ["ConnectionStrings:flight-mongo"] = "mongodb://flight-aspire-mongo:27017",
                ["ConnectionStrings:mongo"] = "mongodb://shared-aspire-mongo:27017",
            }
        );

        Assert.Equal("flight-options-mongo", context.MongoClient.Settings.Server.Host);
    }

    [Fact]
    public void another_modules_connection_string_is_not_used()
    {
        var exception = Assert.Throws<OptionsValidationException>(
            () =>
                Resolve(
                    new()
                    {
                        ["MongoOptions:Flight:DatabaseName"] = "flight_service_read",
                        ["MongoOptions:Passenger:ConnectionString"] = "mongodb://passenger-mongo:27017",
                        ["ConnectionStrings:passenger-mongo"] = "mongodb://passenger-mongo:27017",
                    }
                )
        );

        Assert.Contains("module 'Flight'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void another_modules_database_name_is_not_used()
    {
        var exception = Assert.Throws<OptionsValidationException>(
            () =>
                Resolve(
                    new()
                    {
                        ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                        ["MongoOptions:Passenger:DatabaseName"] = "passenger_service_read",
                    }
                )
        );

        Assert.Contains("MongoOptions:Flight:DatabaseName", exception.Message, StringComparison.Ordinal);
    }
}
