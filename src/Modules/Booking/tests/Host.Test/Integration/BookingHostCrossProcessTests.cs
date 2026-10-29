using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.Models;
using Booking.Data;
using Booking.Host;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.TestBase;
using EventStore.Client;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;
using Xunit.Abstractions;

namespace Host.Test.Integration;

[Collection(BookingHostIntegrationCollection.Name)]
public sealed class BookingHostCrossProcessTests : TestReadBase<Program, BookingReadDbContext>
{
    private readonly BookingHostIntegrationFixture _fixture;

    public BookingHostCrossProcessTests(BookingHostIntegrationFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task persists_and_projects_booking_after_real_flight_and_passenger_grpc_calls()
    {
        var bookingId = Guid.NewGuid();
        var command = new CreateBooking(Guid.NewGuid(), Guid.NewGuid(), "host integration test") { Id = bookingId };

        var result = await Fixture.SendAsync(command);

        result.Should().NotBeNull();
        _fixture.FlightServer.Service.GetByIdCalls.Should().Be(1);
        _fixture.FlightServer.Service.GetAvailableSeatsCalls.Should().Be(1);
        _fixture.FlightServer.Service.ReserveSeatCalls.Should().Be(1);
        _fixture.PassengerServer.Service.GetByIdCalls.Should().Be(1);
        (await Fixture.WaitForPublishing<BookingCreated>()).Should().BeTrue();

        var eventStoreClient = Fixture.ServiceProvider.GetRequiredService<EventStoreClient>();
        var streamId = StreamNameMapper.ToStreamId<global::Booking.Booking.Models.Booking>(bookingId);
        var events = new List<ResolvedEvent>();
        await foreach (
            var resolvedEvent in eventStoreClient.ReadStreamAsync(
                Direction.Forwards,
                streamId,
                StreamPosition.Start,
                cancellationToken: Fixture.CancellationTokenSource.Token
            )
        )
            events.Add(resolvedEvent);

        events
            .Should()
            .Contain(resolvedEvent =>
                resolvedEvent.Event.EventType.Contains(nameof(BookingCreatedDomainEvent), StringComparison.Ordinal)
            );

        (
            await Fixture.WaitUntilAsync(async () =>
            {
                var projection = await Fixture.ExecuteReadContextAsync(db =>
                    db.Booking.Find(booking => booking.BookId == bookingId).FirstOrDefaultAsync()
                );
                return projection is not null;
            })
        ).Should().BeTrue();

        var projectedBooking = await Fixture.ExecuteReadContextAsync(db =>
            db.Booking.Find(booking => booking.BookId == bookingId).FirstOrDefaultAsync()
        );
        projectedBooking!.BookId.Should().Be(bookingId);
        projectedBooking.PassengerInfo.Name.Should().Be("Host Test Passenger");
        projectedBooking.Trip.SeatNumber.Should().Be("1A");
    }
}
