using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Flight.Host.Test;

// Pins the configuration contract the standalone Flight host publishes for sibling tickets
// (ports, service name, per-service databases, broker) without booting anything.
public class FlightHostConfigurationTests
{
    [Fact]
    public void default_settings_should_serve_rest_over_http1_and_grpc_over_a_separate_http2_port()
    {
        var configuration = Load("appsettings.json");

        configuration["Kestrel:Endpoints:Http:Protocols"].Should().Be("Http1");
        configuration["Kestrel:Endpoints:Grpc:Protocols"].Should().Be("Http2");

        var restPort = new Uri(configuration["Kestrel:Endpoints:Http:Url"]!).Port;
        var grpcPort = new Uri(configuration["Kestrel:Endpoints:Grpc:Url"]!).Port;

        restPort.Should().Be(5200);
        grpcPort.Should().Be(5201);
        grpcPort.Should().NotBe(restPort);
    }

    [Fact]
    public void default_settings_should_name_the_service_and_its_broker_identity()
    {
        var configuration = Load("appsettings.json");

        configuration["AppOptions:Name"].Should().Be("Flight-Service");
        configuration["MessageBroker:TransportType"].Should().Be("RabbitMq");
        configuration["MessageBroker:ServiceName"].Should().Be("flight");
        configuration.GetValue<bool>("MessageBroker:UseInbox").Should().BeTrue();
        configuration["Jwt:Audience"].Should().Be("booking-modular-monolith");
    }

    [Fact]
    public void default_settings_should_point_only_at_the_flight_service_databases()
    {
        var configuration = Load("appsettings.json");

        Database(configuration["PostgresOptions:ConnectionString:Flight"]).Should().Be("flight_service");
        Database(configuration["PersistMessageOptions:ConnectionString:Flight"])
            .Should()
            .Be("flight_service_persist_message");
        configuration["MongoOptions:Flight:DatabaseName"].Should().Be("flight_service_read");

        configuration
            .GetSection("PostgresOptions:ConnectionString")
            .GetChildren()
            .Select(c => c.Key)
            .Should()
            .BeEquivalentTo("Flight");
        configuration
            .GetSection("PersistMessageOptions:ConnectionString")
            .GetChildren()
            .Select(c => c.Key)
            .Should()
            .BeEquivalentTo("Flight");
    }

    [Fact]
    public void docker_settings_should_rebind_ports_and_hosts_but_keep_protocols_and_database_names()
    {
        var configuration = Load("appsettings.json", "appsettings.docker.json");

        new Uri(configuration["Kestrel:Endpoints:Http:Url"]!.Replace("+", "localhost", StringComparison.Ordinal))
            .Port.Should()
            .Be(80);
        new Uri(configuration["Kestrel:Endpoints:Grpc:Url"]!.Replace("+", "localhost", StringComparison.Ordinal))
            .Port.Should()
            .Be(81);
        configuration["Kestrel:Endpoints:Http:Protocols"].Should().Be("Http1");
        configuration["Kestrel:Endpoints:Grpc:Protocols"].Should().Be("Http2");

        var postgres = new NpgsqlConnectionStringBuilder(configuration["PostgresOptions:ConnectionString:Flight"]);
        postgres.Host.Should().Be("postgres");
        postgres.Database.Should().Be("flight_service");
        postgres.Username.Should().Be("flight_service", "the service connects with its own database role");

        var persist = new NpgsqlConnectionStringBuilder(configuration["PersistMessageOptions:ConnectionString:Flight"]);
        persist.Host.Should().Be("postgres");
        persist.Database.Should().Be("flight_service_persist_message");
        persist.Username.Should().Be("flight_service");

        configuration["MongoOptions:ConnectionString"].Should().Be("mongodb://mongo:27017");
        configuration["MongoOptions:Flight:DatabaseName"].Should().Be("flight_service_read");
        configuration["MessageBroker:RabbitMq:Host"].Should().Be("rabbitmq");
        configuration["MessageBroker:ServiceName"].Should().Be("flight");
    }

    private static IConfiguration Load(params string[] files)
    {
        var builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory);

        foreach (var file in files)
        {
            builder.AddJsonFile(file, optional: false);
        }

        return builder.Build();
    }

    private static string Database(string? connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString).Database!;
}
