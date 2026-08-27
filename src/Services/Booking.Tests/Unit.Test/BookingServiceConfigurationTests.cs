using Booking.Configuration;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace BookingService.Unit.Test;

public class BookingServiceConfigurationTests
{
    private static IConfigurationRoot BuildConfiguration(string fileName = "booking-service-appsettings.json")
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(fileName, optional: false)
            .Build();
    }

    [Fact]
    public void app_options_should_bind_booking_service_name()
    {
        var configuration = BuildConfiguration();

        var appOptions = configuration.GetSection(nameof(AppOptions)).Get<AppOptions>();

        appOptions.Should().NotBeNull();
        appOptions!.Name.Should().Be("Booking-Service");
    }

    [Fact]
    public void grpc_section_should_bind_flight_and_passenger_addresses()
    {
        var configuration = BuildConfiguration();

        var grpcOptions = configuration.GetSection("Grpc").Get<GrpcOptions>();

        grpcOptions.Should().NotBeNull();
        Uri.IsWellFormedUriString(grpcOptions!.FlightAddress, UriKind.Absolute).Should().BeTrue();
        Uri.IsWellFormedUriString(grpcOptions.PassengerAddress, UriKind.Absolute).Should().BeTrue();
    }

    [Fact]
    public void grpc_section_should_use_grpc_key_expected_by_module()
    {
        var configuration = BuildConfiguration();

        configuration.GetSection("Grpc").Exists().Should().BeTrue();
        configuration.GetSection("GrpcOptions").Exists().Should().BeFalse();
    }

    [Fact]
    public void persist_message_options_should_use_dedicated_booking_outbox_database()
    {
        var configuration = BuildConfiguration();

        var options = configuration.GetSection(nameof(PersistMessageOptions)).Get<PersistMessageOptions>();

        options.Should().NotBeNull();
        options!.Enabled.Should().BeTrue();
        options.ConnectionString.Should().Contain("Database=persist_message_booking");
    }

    [Fact]
    public void event_store_and_mongo_options_should_bind_booking_stores()
    {
        var configuration = BuildConfiguration();

        var eventStoreOptions = configuration.GetSection(nameof(EventStoreOptions)).Get<EventStoreOptions>();
        var mongoOptions = configuration.GetSection(nameof(MongoOptions)).Get<MongoOptions>();

        eventStoreOptions.Should().NotBeNull();
        eventStoreOptions!.ConnectionString.Should().StartWith("esdb://");
        mongoOptions.Should().NotBeNull();
        mongoOptions!.ConnectionString.Should().StartWith("mongodb://");
        mongoOptions.DatabaseName.Should().Be("booking_modular_monolith_read");
    }

    [Fact]
    public void rabbitmq_options_should_bind_shared_exchange()
    {
        var configuration = BuildConfiguration();

        var options = configuration.GetSection(nameof(RabbitMqOptions)).Get<RabbitMqOptions>();

        options.Should().NotBeNull();
        options!.ExchangeName.Should().Be("booking-modular-monolith");
        options.HostName.Should().NotBeNullOrWhiteSpace();
        options.Port.Should().Be(5672);
    }

    [Fact]
    public void configuration_should_not_declare_postgres_write_model_options()
    {
        var configuration = BuildConfiguration();

        configuration.GetSection("PostgresOptions").Exists().Should().BeFalse();
    }

    [Fact]
    public void docker_overrides_should_repoint_rabbitmq_host_only()
    {
        var configuration = BuildConfiguration("booking-service-appsettings.docker.json");

        var options = configuration.GetSection(nameof(RabbitMqOptions)).Get<RabbitMqOptions>();

        options.Should().NotBeNull();
        options!.HostName.Should().Be("rabbitmq");
        options.ExchangeName.Should().Be("booking-modular-monolith");
        configuration.GetChildren().Should().ContainSingle(s => s.Key == nameof(RabbitMqOptions));
    }
}
