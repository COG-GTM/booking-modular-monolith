using System.Net;
using System.Net.Http.Json;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Data;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.TestBase;
using EventStore.Client;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace Host.Test;

[Collection(BookingHostCollection.Name)]
public sealed class BookingHostTests : TestReadBase<Booking.Host.Program, BookingReadDbContext>
{
    private BookingHostFixture HostFixture => (BookingHostFixture)Fixture;

    public BookingHostTests(BookingHostFixture fixture)
        : base(fixture) { }

    [Fact]
    public void CompositionContainsBookingOnly()
    {
        var routes = Fixture
            .ServiceProvider.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(route => route is not null)
            .ToArray();

        Assert.Contains(routes, route => route!.Contains("/booking", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, route => route!.Contains("/flight", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, route => route!.Contains("/passenger", StringComparison.Ordinal));
        Assert.DoesNotContain(routes, route => route!.Contains("/identity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task BookingUsesGrpcAndPersistsEventsAndProjection()
    {
        using var client = Fixture.HttpClient;
        using var response = await client.PostAsJsonAsync(
            "/api/v1/booking",
            new
            {
                PassengerId = Guid.NewGuid(),
                FlightId = Guid.NewGuid(),
                Description = "standalone host test",
            }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains("flight.GetById", HostFixture.GrpcServer.Calls);
        Assert.Contains("flight.GetAvailableSeats", HostFixture.GrpcServer.Calls);
        Assert.Contains("flight.ReserveSeat", HostFixture.GrpcServer.Calls);
        Assert.Contains("passenger.GetById", HostFixture.GrpcServer.Calls);

        var booking = await WaitForBookingAsync();
        var clientStore = Fixture.ServiceProvider.GetRequiredService<EventStoreClient>();
        var stream = StreamNameMapper.ToStreamId<Booking.Booking.Models.Booking>(booking.BookId);
        var events = clientStore.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start, 10);
        var storedEvent = await events.FirstOrDefaultAsync();
        Assert.NotNull(storedEvent.Event);
        Assert.IsType<BookingCreatedDomainEvent>(storedEvent.Deserialize());
        Assert.True(await Fixture.WaitForPublishing<BookingCreated>());
    }

    [Fact]
    public async Task HealthEndpointsAreAnonymousAndReady()
    {
        using var client = Fixture.HttpClient;
        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
    }

    private async Task<Booking.Booking.Models.BookingReadModel> WaitForBookingAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!timeout.IsCancellationRequested)
        {
            var booking = await Fixture.ExecuteReadContextAsync(db =>
                db.Booking.Find(_ => true).FirstOrDefaultAsync(timeout.Token)
            );
            if (booking is not null)
                return booking;
            await Task.Delay(250, timeout.Token);
        }
        throw new TimeoutException("Booking projection did not write a Mongo read model within 30 seconds.");
    }
}
