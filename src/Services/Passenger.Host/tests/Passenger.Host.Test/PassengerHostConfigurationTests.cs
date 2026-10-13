using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Xunit;

namespace Passenger.Host.Test;

// Loads the host's shipped appsettings layers (copied next to the test assembly) the way the
// process does, and pins the deployment contract described in the service's README/ADR.
public class PassengerHostConfigurationTests
{
    private static IConfiguration Load(string? environment = null)
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
    public void kestrel_serves_rest_on_5300_and_grpc_on_a_dedicated_http2_port()
    {
        var configuration = Load();

        configuration["Kestrel:Endpoints:Http:Url"].Should().Be("http://localhost:5300");
        configuration["Kestrel:Endpoints:Http:Protocols"].Should().Be("Http1AndHttp2");
        configuration["Kestrel:Endpoints:Grpc:Url"].Should().Be("http://localhost:5301");
        configuration["Kestrel:Endpoints:Grpc:Protocols"].Should().Be("Http2");
    }

    [Fact]
    public void stores_are_owned_by_the_passenger_service()
    {
        var configuration = Load();

        configuration["PostgresOptions:ConnectionString:Passenger"].Should().Contain("Database=passenger_service;");
        configuration["PersistMessageOptions:ConnectionString:Passenger"]
            .Should()
            .Contain("Database=passenger_service_persist_message;");
        configuration["MongoOptions:Passenger:DatabaseName"].Should().Be("passenger_service_read");
        configuration.IsModuleBackgroundProcessingEnabled("Passenger").Should().BeTrue();
    }

    // The shared HealthOptions-driven checks bind PostgresOptions:ConnectionString as a single string and
    // register a second "self" check, neither of which works with the per-module layout this host uses.
    [Fact]
    public void shared_health_check_ui_stays_disabled()
    {
        var configuration = Load();

        configuration.GetValue<bool>("HealthOptions:Enabled").Should().BeFalse();
        Load("docker").GetValue<bool>("HealthOptions:Enabled").Should().BeFalse();
    }

    [Fact]
    public void broker_consumes_user_created_on_a_passenger_prefixed_rabbitmq_queue_behind_the_inbox()
    {
        var options = Load().GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName);

        options.TransportType.Should().Be(TransportType.RabbitMq);
        options.ServiceName.Should().Be("passenger");
        options.UseInbox.Should().BeTrue();
        BuildingBlocks
            .MassTransit.Extensions.CreateEndpointNameFormatter(options)
            .Consumer<RegisterNewUserHandler>()
            .Should()
            .Be("passenger-register-new-user-handler");
    }

    [Fact]
    public void jwt_validates_tokens_issued_by_the_monolith_identity_server()
    {
        var jwt = Load().GetOptions<JwtBearerOptions>("Jwt");

        jwt.Authority.Should().Be("https://localhost:3000");
        jwt.Audience.Should().Be("booking-modular-monolith");
    }

    [Fact]
    public void docker_layer_binds_container_ports_and_compose_hostnames()
    {
        var configuration = Load("docker");

        configuration["Kestrel:Endpoints:Http:Url"].Should().Be("http://+:80");
        configuration["Kestrel:Endpoints:Grpc:Url"].Should().Be("http://+:81");
        configuration["Kestrel:Endpoints:Grpc:Protocols"].Should().Be("Http2");
        configuration["PostgresOptions:ConnectionString:Passenger"].Should().Contain("Server=postgres;");
        configuration["PostgresOptions:ConnectionString:Passenger"].Should().Contain("Database=passenger_service;");
        configuration["PersistMessageOptions:ConnectionString:Passenger"].Should().Contain("Server=postgres;");
        configuration["PersistMessageOptions:ConnectionString:Passenger"]
            .Should()
            .Contain("Database=passenger_service_persist_message;");
        configuration["MongoOptions:ConnectionString"].Should().Be("mongodb://mongo:27017");
        configuration["MongoOptions:Passenger:DatabaseName"].Should().Be("passenger_service_read");

        var broker = configuration.GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName);
        broker.TransportType.Should().Be(TransportType.RabbitMq);
        broker.ServiceName.Should().Be("passenger");
        broker.RabbitMq.Host.Should().Be("rabbitmq");

        var jwt = configuration.GetOptions<JwtBearerOptions>("Jwt");
        jwt.Authority.Should().Be("https://localhost:3000", "tokens are still validated against the public issuer");
        jwt.MetadataAddress.Should().Be("http://booking_modular_monolith:80/.well-known/openid-configuration");
        jwt.Audience.Should().Be("booking-modular-monolith");
    }
}
