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

// Resolved read contexts (not only their options) must point at the module's own database; MongoClient
// connects lazily so no Mongo server is needed here.
public class PerModuleMongoContextTests
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

    [Fact]
    public void resolved_read_contexts_should_target_their_own_databases()
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

        using var scope = provider.CreateScope();

        var flightContext = scope.ServiceProvider.GetRequiredService<FlightReadDbContext>();
        var otherContext = scope.ServiceProvider.GetRequiredService<OtherReadDbContext>();

        flightContext.Database.DatabaseNamespace.DatabaseName.Should().Be("flight_read");
        otherContext.Database.DatabaseNamespace.DatabaseName.Should().Be("other_read");
        flightContext.Should().NotBeSameAs(otherContext);
    }

    [Fact]
    public void aspire_connection_string_without_database_should_keep_module_read_database_name()
    {
        var provider = Register(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight_read",
                ["ConnectionStrings:flight-read"] = "mongodb://root:secret@mongo:27017/?authSource=admin",
            },
            builder => builder.AddMongoDbContext<FlightReadDbContext>(nameof(Flight))
        );

        var options = provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(nameof(Flight));

        options.ConnectionString.Should().Be("mongodb://root:secret@mongo:27017/?authSource=admin");
        options.DatabaseName.Should().Be("flight_read");
    }

    [Fact]
    public void module_aspire_connection_string_should_win_over_shared_server_connection_string()
    {
        var provider = Register(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:Flight:DatabaseName"] = "flight_read",
                ["ConnectionStrings:mongo"] = "mongodb://root:secret@mongo:27017/shared?authSource=admin",
                ["ConnectionStrings:flight-read"] = "mongodb://root:secret@mongo:27017/flight-read?authSource=admin",
            },
            builder => builder.AddMongoDbContext<FlightReadDbContext>(nameof(Flight))
        );

        var options = provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(nameof(Flight));

        options.ConnectionString.Should().Be("mongodb://root:secret@mongo:27017/flight-read?authSource=admin");
        options.DatabaseName.Should().Be("flight-read");
    }

    [Fact]
    public void aspire_connection_name_should_be_kebab_case_module_name_with_read_suffix()
    {
        var provider = Register(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:OtherModule:DatabaseName"] = "other_read",
                ["ConnectionStrings:other-module-read"] = "mongodb://mongo:27017/other-module-read",
            },
            builder => builder.AddMongoDbContext<OtherReadDbContext>("OtherModule")
        );

        var options = provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("OtherModule");

        options.ConnectionString.Should().Be("mongodb://mongo:27017/other-module-read");
        options.DatabaseName.Should().Be("other-module-read");
    }

    [Fact]
    public void module_read_database_name_should_override_shared_default()
    {
        var provider = Register(
            new Dictionary<string, string?>
            {
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
                ["MongoOptions:DatabaseName"] = "shared_read",
                ["MongoOptions:Flight:DatabaseName"] = "flight_read",
            },
            builder => builder.AddMongoDbContext<FlightReadDbContext>(nameof(Flight))
        );

        var options = provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(nameof(Flight));

        options.DatabaseName.Should().Be("flight_read");
    }

    [Fact]
    public void missing_mongo_server_connection_string_should_fail_validation()
    {
        var provider = Register(
            new Dictionary<string, string?> { ["MongoOptions:Flight:DatabaseName"] = "flight_read" },
            builder => builder.AddMongoDbContext<FlightReadDbContext>(nameof(Flight))
        );

        var act = () => provider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(nameof(Flight));

        act.Should().Throw<OptionsValidationException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void empty_connection_name_should_be_rejected(string? connectionName)
    {
        var builder = WebApplication.CreateBuilder();

        var act = () => builder.AddMongoDbContext<FlightReadDbContext>(connectionName!);

        act.Should().Throw<ArgumentException>();
    }
}
