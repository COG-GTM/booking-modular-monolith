using System.Net;
using System.Net.Http.Json;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Data;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.PersistMessageProcessor;
using Contracts.Grpc.Flight;
using Contracts.Grpc.Passenger;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Xunit;
using BookingAggregate = Booking.Booking.Models.Booking;

namespace Booking.Host.Test;

public class BookingHostTests : BookingHostTestBase
{
    public BookingHostTests(BookingHostFixture fixture)
        : base(fixture) { }

    [Fact]
    public async Task host_boots_standalone_and_serves_root_and_liveness()
    {
        var client = Fixture.HttpClient;

        (await client.GetStringAsync("/")).Should().Be("Booking-Service");
        (await client.GetAsync("/alive")).StatusCode.Should().Be(HttpStatusCode.OK);
        var health = await client.GetAsync("/health");
        health.StatusCode.Should().Be(HttpStatusCode.OK, await health.Content.ReadAsStringAsync());
    }

    [Fact]
    public void host_composes_booking_module_with_own_stores_and_grpc_clients()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var services = scope.ServiceProvider;

        services.GetRequiredService<FlightGrpcService.FlightGrpcServiceClient>().Should().NotBeNull();
        services.GetRequiredService<PassengerGrpcService.PassengerGrpcServiceClient>().Should().NotBeNull();
        services.GetRequiredService<IEventStoreDBRepository<BookingAggregate>>().Should().NotBeNull();
        services.GetRequiredService<BookingReadDbContext>().Should().NotBeNull();
        services.GetRequiredService<IEventDispatcher<BookingRoot>>().Should().NotBeNull();
        services.GetRequiredService<IPersistMessageProcessor<BookingRoot>>().Should().NotBeNull();
        services.GetServices<IEventMapper>().Should().ContainSingle(m => m is BookingEventMapper);

        var configuration = services.GetRequiredService<IConfiguration>();
        configuration["Grpc:FlightAddress"].Should().Be(Host.FlightAddress);
        configuration["Grpc:PassengerAddress"].Should().Be(Host.PassengerAddress);
    }

    [Fact]
    public async Task create_booking_calls_flight_and_passenger_over_grpc_and_persists_to_event_store()
    {
        var command = new CreateBooking(Guid.NewGuid(), Guid.NewGuid(), "AB-496 standalone booking");

        var result = await Fixture.SendAsync(command);

        result.Should().NotBeNull();
        Host.Flight.RequestedFlightIds.Should().Contain(command.FlightId.ToString());
        Host.Passenger.RequestedPassengerIds.Should().Contain(command.PassengerId.ToString());
        Host.Flight.ReservedSeats.Should()
            .Contain(r =>
                r.FlightId == command.FlightId.ToString() && r.SeatNumber == FakeFlightGrpcService.AvailableSeatNumber
            );

        var booking = await ExecuteScopeAsync(sp =>
            sp.GetRequiredService<IEventStoreDBRepository<BookingAggregate>>().Find(command.Id, CancellationToken.None)
        );
        booking.Should().NotBeNull();
        booking!.Id.Should().Be(command.Id);
        booking.PassengerInfo.Name.Should().Be(FakePassengerGrpcService.PassengerName);
        booking.Trip.SeatNumber.Should().Be(FakeFlightGrpcService.AvailableSeatNumber);
        booking.Trip.Description.Should().Be(command.Description);

        (await Fixture.WaitForPublishing<BookingCreated>()).Should().BeTrue();

        // The EventStoreDB all-streams subscription projects BookingCreated into booking_service_read.
        var projected = await WaitUntilAsync(
            () =>
                Fixture.ExecuteReadContextAsync(ctx => ctx.Booking.AsQueryable().AnyAsync(b => b.BookId == command.Id))
        );
        projected.Should().BeTrue();
    }

    [Fact]
    public async Task create_booking_endpoint_is_served_by_the_standalone_host()
    {
        var request = new CreateBookingRequestDto(Guid.NewGuid(), Guid.NewGuid(), "AB-496 via REST");

        var response = await Fixture.HttpClient.PostAsJsonAsync("/api/v1.0/booking", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<CreateBookingResponseDto>()).Should().NotBeNull();
        Host.Flight.ReservedSeats.Should().Contain(r => r.FlightId == request.FlightId.ToString());
        Host.Passenger.RequestedPassengerIds.Should().Contain(request.PassengerId.ToString());
    }

    private Task<T> ExecuteScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        return action(scope.ServiceProvider);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, int timeoutSeconds = 30)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }
}
