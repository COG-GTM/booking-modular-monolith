using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Integration.Test.Host;

// Flight.Host owns its own stores: its appsettings must point at the flight_service databases, not at the
// monolith's flight_modular_monolith / flight_read ones, in every environment profile. These tests read
// the host's appsettings files copied next to the test assembly and do not start any container.
public class FlightHostConfigurationTests
{
    private const string MonolithPostgresDatabase = "flight_modular_monolith";

    private static IConfiguration LoadHostConfiguration(string? environment = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false);

        if (environment is not null)
        {
            builder.AddJsonFile($"appsettings.{environment}.json", optional: false);
        }

        return builder.Build();
    }

    [Fact]
    public void app_name_should_identify_the_flight_service()
    {
        var configuration = LoadHostConfiguration();

        configuration.GetOptions<AppOptions>(nameof(AppOptions)).Name.Should().Be("Flight-Service");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("docker")]
    public void flight_write_store_should_be_the_flight_service_database(string? environment)
    {
        var configuration = LoadHostConfiguration(environment);

        var connectionString = new NpgsqlConnectionStringBuilder(
            configuration["PostgresOptions:ConnectionString:Flight"]
        );

        connectionString.Database.Should().Be("flight_service");
        connectionString.Database.Should().NotBe(MonolithPostgresDatabase);
        connectionString.Username.Should().Be("flight");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("docker")]
    public void flight_read_store_should_be_the_flight_service_read_database(string? environment)
    {
        var configuration = LoadHostConfiguration(environment);

        configuration["MongoOptions:Flight:DatabaseName"].Should().Be("flight_service_read");
        configuration["MongoOptions:ConnectionString"].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void should_only_configure_the_flight_module_stores()
    {
        var configuration = LoadHostConfiguration();

        configuration
            .GetSection("PostgresOptions:ConnectionString")
            .GetChildren()
            .Select(x => x.Key)
            .Should()
            .Equal("Flight");
        configuration
            .GetSection("MongoOptions")
            .GetChildren()
            .Select(x => x.Key)
            .Should()
            .BeEquivalentTo("ConnectionString", "Flight");
        configuration["EventStoreOptions:ConnectionString"].Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("docker")]
    public void rabbitmq_options_should_be_complete(string? environment)
    {
        var configuration = LoadHostConfiguration(environment);

        var rabbitMqOptions = configuration.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));

        rabbitMqOptions.HostName.Should().NotBeNullOrWhiteSpace();
        rabbitMqOptions.ExchangeName.Should().Be("FlightService");
        rabbitMqOptions.UserName.Should().NotBeNullOrWhiteSpace();
        rabbitMqOptions.Password.Should().NotBeNullOrWhiteSpace();
        rabbitMqOptions.Port.Should().Be(5672);
    }

    [Fact]
    public void docker_profile_should_target_the_compose_services()
    {
        var configuration = LoadHostConfiguration("docker");

        new NpgsqlConnectionStringBuilder(configuration["PostgresOptions:ConnectionString:Flight"])
            .Host.Should()
            .Be("postgres");
        configuration["MongoOptions:ConnectionString"].Should().Be("mongodb://mongo:27017");
        configuration["RabbitMqOptions:HostName"].Should().Be("rabbitmq");
    }

    [Fact]
    public void jwt_should_still_trust_the_monolith_identity_authority()
    {
        var configuration = LoadHostConfiguration();

        configuration["Jwt:Authority"].Should().Be("https://localhost:3000");
        configuration["Jwt:Audience"].Should().Be("booking-modular-monolith");
    }
}
