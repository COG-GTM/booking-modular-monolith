using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Data;
using Booking.Host.Extensions;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Serialization;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.TestBase;
using EventStore.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
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
        using var eventJson = JsonDocument.Parse(storedEvent.Event.Data.ToArray());
        Assert.NotEqual(Guid.Empty, eventJson.RootElement.GetProperty("Id").GetGuid());
        var bookingEvent = Assert.IsType<BookingCreatedDomainEvent>(storedEvent.Deserialize());
        Assert.NotEqual(Guid.Empty, bookingEvent.Id);
        Assert.Equal(HostFixture.GrpcServer.FlightNumber, bookingEvent.Trip.FlightNumber);
        Assert.Equal(HostFixture.GrpcServer.AircraftId, bookingEvent.Trip.AircraftId);
        Assert.Equal(HostFixture.GrpcServer.DepartureAirportId, bookingEvent.Trip.DepartureAirportId);
        Assert.Equal(HostFixture.GrpcServer.ArriveAirportId, bookingEvent.Trip.ArriveAirportId);
        Assert.Equal(HostFixture.GrpcServer.FlightDate, bookingEvent.Trip.FlightDate);
        Assert.Equal(100, bookingEvent.Trip.Price);
        Assert.Equal("standalone host test", bookingEvent.Trip.Description);
        Assert.Equal("33F", bookingEvent.Trip.SeatNumber);
        Assert.Equal(bookingEvent.Id, booking.BookId);
        Assert.Equal(HostFixture.GrpcServer.FlightNumber, booking.Trip.FlightNumber);
        Assert.Equal(HostFixture.GrpcServer.AircraftId, booking.Trip.AircraftId);
        Assert.Equal(HostFixture.GrpcServer.DepartureAirportId, booking.Trip.DepartureAirportId);
        Assert.Equal(HostFixture.GrpcServer.ArriveAirportId, booking.Trip.ArriveAirportId);
        Assert.Equal(HostFixture.GrpcServer.FlightDate, booking.Trip.FlightDate);
        Assert.Equal(100, booking.Trip.Price);
        Assert.Equal("standalone host test", booking.Trip.Description);
        Assert.Equal("33F", booking.Trip.SeatNumber);
        Assert.Equal("Host test passenger", booking.PassengerInfo.Name);
        Assert.True(await Fixture.WaitForPublishing<BookingCreated>());
    }

    [Fact]
    public async Task HealthEndpointsAreAnonymousAndReady()
    {
        using var client = Fixture.HttpClient;
        using var live = await client.GetAsync("/health/live");
        await WaitForReadyAsync(client);
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        var checks = Fixture
            .ServiceProvider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;
        Assert.Contains(checks, registration => registration.Name == "eventstore-subscription");
        var subscriptionState = Fixture.ServiceProvider.GetRequiredService<EventStoreDBSubscriptionState>();
        Assert.True(subscriptionState.IsSubscribed);
        Assert.NotNull(subscriptionState.LastChangedAt);
    }

    [Fact]
    public void SubscriptionReadinessCheckIsOmittedWhenBackgroundProcessingIsDisabled()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Modules:Booking:BackgroundProcessingEnabled"] = "false" }
        );
        builder.Services.AddBookingHealthChecks(builder.Configuration, builder.Environment);
        using var serviceProvider = builder.Services.BuildServiceProvider();
        var registrations = serviceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations;

        Assert.DoesNotContain(registrations, registration => registration.Name == "eventstore-subscription");
    }

    [Fact]
    public async Task SubscriptionReadinessCheckIsUnhealthyWhenStateIsNotRegistered()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Modules:Booking:BackgroundProcessingEnabled"] = "true" }
        );
        builder.Services.AddBookingHealthChecks(builder.Configuration, builder.Environment);
        using var serviceProvider = builder.Services.BuildServiceProvider();
        var healthChecks = serviceProvider.GetRequiredService<HealthCheckService>();
        var report = await healthChecks.CheckHealthAsync(check => check.Name == "eventstore-subscription");

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal(
            "EventStoreDB $all subscription is not active",
            report.Entries["eventstore-subscription"].Description
        );
    }

    private static async Task WaitForReadyAsync(HttpClient client)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            using var response = await client.GetAsync("/health/ready");
            if (response.StatusCode == HttpStatusCode.OK)
                return;
            await Task.Delay(250);
        }

        throw new TimeoutException("Booking.Host did not become ready within 30 seconds.");
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
