using BuildingBlocks.Mongo;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;
using global::Flight.Data;

// Read contexts are bound to MongoOptions named after their module; the options (not a live Mongo connection)
// are asserted here, the real read databases are covered by the module integration tests.
public class PerModuleMongoTests
{
    private sealed class OtherReadDbContext(IOptions<MongoOptions> options) : MongoDbContext(options);

    private static IServiceProvider Register(
        Dictionary<string, string?> configuration,
        params Action<WebApplicationBuilder>[] registrations
    )
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(configuration);

        foreach (var registration in registrations)
        {
            registration(builder);
        }

        return builder.Services.BuildServiceProvider();
    }

    private static MongoOptions OptionsFor(IServiceProvider provider, string connectionName)
    {
        return provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(connectionName);
    }

    [Fact]
    public void each_module_read_context_should_use_its_own_database()
    {
        var provider = Register(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight_read",
                ["MongoOptions:Other:DatabaseName"] = "other_read",
            },
            builder => builder.AddMongoDbContext<FlightReadDbContext>(nameof(Flight)),
            builder => builder.AddMongoDbContext<OtherReadDbContext>("Other")
        );

        var flightOptions = OptionsFor(provider, nameof(Flight));
        var otherOptions = OptionsFor(provider, "Other");

        flightOptions.ConnectionString.Should().Be("mongodb://localhost:27017");
        flightOptions.DatabaseName.Should().Be("flight_read");
        otherOptions.ConnectionString.Should().Be("mongodb://localhost:27017");
        otherOptions.DatabaseName.Should().Be("other_read");

        // no module-agnostic read context is registered any more
        provider.GetService<IMongoDbContext>().Should().BeNull();
    }

    [Fact]
    public void aspire_connection_string_should_override_module_read_database()
    {
        var provider = Register(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight_read",
                ["ConnectionStrings:flight-read"] = "mongodb://root:secret@mongo:27017/flight-read?authSource=admin",
            },
            builder => builder.AddMongoDbContext<FlightReadDbContext>(nameof(Flight))
        );

        var flightOptions = OptionsFor(provider, nameof(Flight));

        flightOptions.ConnectionString.Should().Be("mongodb://root:secret@mongo:27017/flight-read?authSource=admin");
        flightOptions.DatabaseName.Should().Be("flight-read");
    }

    [Fact]
    public void shared_mongo_server_connection_string_should_keep_module_read_database()
    {
        var provider = Register(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight_read",
                ["MongoOptions:Other:DatabaseName"] = "other_read",
                ["ConnectionStrings:mongo"] = "mongodb://root:secret@mongo:27017/shared?authSource=admin",
            },
            builder => builder.AddMongoDbContext<FlightReadDbContext>(nameof(Flight)),
            builder => builder.AddMongoDbContext<OtherReadDbContext>("Other")
        );

        var flightOptions = OptionsFor(provider, nameof(Flight));
        var otherOptions = OptionsFor(provider, "Other");

        flightOptions.ConnectionString.Should().Be("mongodb://root:secret@mongo:27017/shared?authSource=admin");
        flightOptions.DatabaseName.Should().Be("flight_read");
        otherOptions.DatabaseName.Should().Be("other_read");
    }

    [Fact]
    public void module_without_read_database_name_should_fail_validation()
    {
        var provider = Register(
            new Dictionary<string, string?> { ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017" },
            builder => builder.AddMongoDbContext<FlightReadDbContext>(nameof(Flight))
        );

        var act = () => OptionsFor(provider, nameof(Flight));

        act.Should().Throw<OptionsValidationException>();
    }
}
