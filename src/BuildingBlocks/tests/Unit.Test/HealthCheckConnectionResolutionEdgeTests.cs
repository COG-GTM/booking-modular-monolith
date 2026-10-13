using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class HealthCheckConnectionResolutionEdgeTests
{
    private static IConfiguration Build(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Dictionary<string, string?> Resolve(IConfiguration configuration) =>
        BuildingBlocks
            .HealthCheck.Extensions.GetPostgresConnections(configuration)
            .ToDictionary(c => c.Name, c => c.Resolve(configuration));

    [Fact]
    public void falls_back_to_options_when_no_aspire_connection_strings_are_configured()
    {
        var configuration = Build(
            new()
            {
                ["PostgresOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight_service",
                ["PersistMessageOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight_persist",
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
            }
        );

        Resolve(configuration)
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string?>
                {
                    ["postgres-flight"] = "Host=localhost;Database=flight_service",
                    ["postgres-flight-persist-message"] = "Host=localhost;Database=flight_persist",
                }
            );
        BuildingBlocks
            .HealthCheck.Extensions.GetMongoConnectionString(configuration)
            .Should()
            .Be("mongodb://localhost:27017");
    }

    [Fact]
    public void module_keys_are_kebab_cased_to_match_aspire_resource_names()
    {
        var configuration = Build(
            new()
            {
                ["PostgresOptions:ConnectionString:FlightBooking"] = "Host=localhost;Database=fb",
                ["ConnectionStrings:flight-booking"] = "Host=postgres;Database=fb",
                ["PersistMessageOptions:ConnectionString:FlightBooking"] = "Host=localhost;Database=fb_pm",
                ["ConnectionStrings:flight-booking-persist-message"] = "Host=postgres;Database=fb_pm",
            }
        );

        Resolve(configuration)
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string?>
                {
                    ["postgres-flight-booking"] = "Host=postgres;Database=fb",
                    ["postgres-flight-booking-persist-message"] = "Host=postgres;Database=fb_pm",
                }
            );
    }

    [Fact]
    public void persist_message_store_configured_in_both_sources_is_registered_once()
    {
        var configuration = Build(
            new()
            {
                ["PersistMessageOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight_persist",
                ["ConnectionStrings:Flight-Persist-Message"] = "Host=postgres;Database=flight_persist",
            }
        );

        var connections = BuildingBlocks.HealthCheck.Extensions.GetPostgresConnections(configuration).ToList();

        connections.Should().ContainSingle();
        connections[0].Name.Should().Be("postgres-flight-persist-message");
        connections[0].Resolve(configuration).Should().Be("Host=postgres;Database=flight_persist");
    }

    [Fact]
    public void aspire_only_module_store_without_options_entry_is_not_registered()
    {
        var configuration = Build(new() { ["ConnectionStrings:flight"] = "Host=postgres;Database=flight_service" });

        Resolve(configuration).Should().BeEmpty();
    }

    [Fact]
    public void empty_values_in_both_sources_skip_the_check()
    {
        var configuration = Build(
            new()
            {
                ["PostgresOptions:ConnectionString:Flight"] = "",
                ["ConnectionStrings:flight"] = "",
                ["PersistMessageOptions:ConnectionString:Flight"] = "",
                ["ConnectionStrings:flight-persist-message"] = "",
            }
        );

        Resolve(configuration).Should().BeEmpty();
    }

    [Fact]
    public void resolvers_read_the_configuration_they_are_given_at_call_time()
    {
        var registrationTime = Build(
            new() { ["PostgresOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight_service" }
        );
        var runtime = Build(
            new()
            {
                ["PostgresOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight_service",
                ["ConnectionStrings:flight"] = "Host=postgres;Database=flight_service",
            }
        );

        var (name, resolve) = BuildingBlocks.HealthCheck.Extensions.GetPostgresConnections(registrationTime).Single();

        name.Should().Be("postgres-flight");
        resolve(registrationTime).Should().Be("Host=localhost;Database=flight_service");
        resolve(runtime).Should().Be("Host=postgres;Database=flight_service");
    }

    [Fact]
    public void mongo_check_is_registered_from_aspire_connection_string_alone()
    {
        var configuration = Build(new() { ["ConnectionStrings:mongo"] = "mongodb://mongo:27017" });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);

        services.AddCustomHealthCheck();

        var registrations = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        registrations.Select(r => r.Name).Should().BeEquivalentTo("self", "mongodb");
        registrations
            .Single(r => r.Name == "mongodb")
            .Tags.Should()
            .Contain(BuildingBlocks.HealthCheck.Extensions.ReadyTag);
    }

    [Fact]
    public void aspire_only_persist_message_store_is_registered_as_a_readiness_check()
    {
        var configuration = Build(
            new() { ["ConnectionStrings:flight-persist-message"] = "Host=postgres;Database=flight_persist" }
        );
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);

        services.AddCustomHealthCheck();

        var registrations = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        registrations.Select(r => r.Name).Should().BeEquivalentTo("self", "postgres-flight-persist-message");
    }
}
