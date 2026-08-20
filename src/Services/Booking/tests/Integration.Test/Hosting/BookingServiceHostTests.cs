using Booking;
using Booking.Data;
using BuildingBlocks.Core;
using BuildingBlocks.MassTransit;
using BuildingBlocks.TestBase;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookingService.Integration.Test.Hosting;

public class BookingServiceHostTests : BookingServiceIntegrationTestBase
{
    public BookingServiceHostTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture) : base(
        integrationTestFixture)
    {
    }

    [Fact]
    public void should_register_booking_read_db_context()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var readDbContext = scope.ServiceProvider.GetService<BookingReadDbContext>();

        readDbContext.Should().NotBeNull();
    }

    [Fact]
    public void should_register_only_booking_module_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var registrations = scope.ServiceProvider.GetServices<IEventMapperRegistration>().ToList();

        registrations.Should().ContainSingle();
        registrations.Single().Mapper.Should().BeOfType<BookingEventMapper>();
    }

    [Fact]
    public void should_configure_bus_with_rabbitmq_transport()
    {
        var bus = Fixture.ServiceProvider.GetRequiredService<IBus>();

        bus.Address.Scheme.Should().Be("rabbitmq");
    }

    [Fact]
    public void should_bind_rabbitmq_options_from_configuration()
    {
        var rabbitMqOptions = Fixture.ServiceProvider.GetRequiredService<RabbitMqOptions>();

        rabbitMqOptions.Should().NotBeNull();
        rabbitMqOptions.HostName.Should().NotBeNullOrEmpty();
        rabbitMqOptions.UserName.Should().NotBeNullOrEmpty();
        rabbitMqOptions.Password.Should().NotBeNullOrEmpty();
        rabbitMqOptions.Port.Should().NotBeNull();
        rabbitMqOptions.ExchangeName.Should().Be("booking");
    }
}
