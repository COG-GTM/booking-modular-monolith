using BuildingBlocks.EFCore;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;
using global::Flight.Data;

// Every module resolves its own Postgres connection through GetPostgresConnectionString: the Aspire
// ConnectionStrings:{kebab-name} entry wins, otherwise PostgresOptions:ConnectionString:{Name} is used.
public class PostgresConnectionStringTests
{
    private const string FlightOptionsConnectionString =
        "Host=localhost;Database=flight_modular_monolith;Username=flight;Password=flight";
    private const string FlightAspireConnectionString =
        "Host=postgres;Port=5432;Database=flight;Username=flight;Password=secret";

    private static IConfiguration Configuration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void should_fall_back_to_postgres_options_for_the_module()
    {
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = FlightOptionsConnectionString,
                ["PostgresOptions:ConnectionString:Passenger"] = "Host=localhost;Database=passenger",
            }
        );

        configuration.GetPostgresConnectionString(nameof(Flight)).Should().Be(FlightOptionsConnectionString);
    }

    [Fact]
    public void aspire_connection_string_should_win_over_postgres_options()
    {
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = FlightOptionsConnectionString,
                ["ConnectionStrings:flight"] = FlightAspireConnectionString,
            }
        );

        configuration.GetPostgresConnectionString(nameof(Flight)).Should().Be(FlightAspireConnectionString);
    }

    [Fact]
    public void aspire_connection_name_should_be_the_kebab_case_of_the_module_name()
    {
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:persist-message"] = "Host=localhost;Database=persist_message",
            }
        );

        configuration
            .GetPostgresConnectionString("PersistMessage")
            .Should()
            .Be("Host=localhost;Database=persist_message");
    }

    [Fact]
    public void aspire_connection_string_of_another_module_should_not_be_used()
    {
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:passenger"] = "Host=postgres;Database=passenger",
                ["PostgresOptions:ConnectionString:Flight"] = FlightOptionsConnectionString,
            }
        );

        configuration.GetPostgresConnectionString(nameof(Flight)).Should().Be(FlightOptionsConnectionString);
    }

    [Fact]
    public void missing_connection_string_should_throw_with_the_connection_name()
    {
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Passenger"] = "Host=localhost;Database=passenger",
            }
        );

        var act = () => configuration.GetPostgresConnectionString(nameof(Flight));

        act.Should().Throw<ArgumentException>().WithMessage("*'Flight'*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void empty_connection_name_should_throw(string? connectionName)
    {
        var configuration = Configuration(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = FlightOptionsConnectionString,
            }
        );

        var act = () => configuration.GetPostgresConnectionString(connectionName!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void module_db_context_should_use_the_module_connection_string()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = FlightOptionsConnectionString,
                ["PostgresOptions:ConnectionString:Passenger"] = "Host=localhost;Database=passenger",
            }
        );

        builder.AddCustomDbContext<FlightDbContext>(nameof(Flight));

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<FlightDbContext>();

        context.Database.GetConnectionString().Should().Be(FlightOptionsConnectionString);
    }

    [Fact]
    public void module_db_context_should_prefer_the_aspire_connection_string()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = FlightOptionsConnectionString,
                ["ConnectionStrings:flight"] = FlightAspireConnectionString,
            }
        );

        builder.AddCustomDbContext<FlightDbContext>(nameof(Flight));

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<FlightDbContext>();

        context.Database.GetConnectionString().Should().Be(FlightAspireConnectionString);
    }
}
