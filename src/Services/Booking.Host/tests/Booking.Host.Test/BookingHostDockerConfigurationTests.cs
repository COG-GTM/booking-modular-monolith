using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Booking.Host.Test;

// docker-compose runs the host with ASPNETCORE_ENVIRONMENT=docker. These tests load that layer the way
// the container does and pin the post-decommission contract (AB-505): no setting may still point at the
// retired booking_modular_monolith container; tokens are discovered from identity-service instead.
public class BookingHostDockerConfigurationTests
{
    private const string DecommissionedMonolithContainer = "booking_modular_monolith";

    private static IConfigurationRoot LoadDockerLayer() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.docker.json", optional: false)
            .Build();

    [Fact]
    public void default_layer_validates_tokens_issued_by_the_identity_service()
    {
        var jwt = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build()
            .GetOptions<JwtBearerOptions>("Jwt");

        jwt.Authority.Should().Be("http://localhost:5100", "local dev tokens come from the Identity host, not src/Api");
        jwt.Audience.Should().Be("booking-modular-monolith");
    }

    [Fact]
    public void docker_layer_discovers_tokens_from_identity_service_but_validates_the_public_issuer()
    {
        var jwt = LoadDockerLayer().GetOptions<JwtBearerOptions>("Jwt");

        jwt.MetadataAddress.Should().Be("http://identity-service/.well-known/openid-configuration");
        jwt.Authority.Should().Be("https://localhost:3000", "tokens are still validated against the public issuer");
        jwt.Audience.Should().Be("booking-modular-monolith");
    }

    [Fact]
    public void docker_layer_calls_flight_and_passenger_over_their_service_grpc_ports()
    {
        var configuration = LoadDockerLayer();

        configuration["Grpc:FlightAddress"].Should().Be("http://flight-service:81");
        configuration["Grpc:PassengerAddress"].Should().Be("http://passenger-service:81");
    }

    [Fact]
    public void docker_layer_no_longer_references_the_decommissioned_monolith_container()
    {
        var configuration = LoadDockerLayer();

        var stale = configuration
            .AsEnumerable()
            .Where(pair => pair.Value?.Contains(DecommissionedMonolithContainer, StringComparison.Ordinal) == true)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToList();

        stale.Should().BeEmpty("src/Api was removed in AB-505, so nothing may dial its container");
    }

    [Fact]
    public void docker_layer_keeps_booking_owned_stores_on_the_compose_service_names()
    {
        var configuration = LoadDockerLayer();

        configuration["MongoOptions:ConnectionString"].Should().Be("mongodb://mongo:27017");
        configuration["MongoOptions:Booking:DatabaseName"].Should().Be("booking_service_read");
        configuration["EventStoreOptions:ConnectionString"].Should().Be("esdb://eventstore:2113?tls=false");
        configuration["MessageBroker:RabbitMq:Host"].Should().Be("rabbitmq");
        configuration["PersistMessageOptions:ConnectionString:Booking"]
            .Should()
            .Contain("Server=postgres;")
            .And.Contain("Database=booking_service_persist_message;");
    }
}
