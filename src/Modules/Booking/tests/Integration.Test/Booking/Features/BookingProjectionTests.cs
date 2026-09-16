using Api;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.ValueObjects;
using Booking.Data;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.TestBase;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Xunit;

namespace Integration.Test.Booking.Features;

public class BookingProjectionTests : BookingIntegrationTestBase
{
    public BookingProjectionTests(TestReadFixture<Program, BookingReadDbContext> integrationTestFixture) : base(
        integrationTestFixture)
    {
    }

    [Fact]
    public async Task should_project_booking_created_event_to_mongo_read_model()
    {
        // Arrange
        var bookingId = NewId.NextGuid();
        var @event = new BookingCreatedDomainEvent(
            PassengerInfo.Of("Sam"),
            Trip.Of("BD467", NewId.NextGuid(), NewId.NextGuid(), NewId.NextGuid(), DateTime.Now, 120m, "desc", "12A"))
        {
            Id = bookingId
        };

        var streamEvent = new StreamEvent<BookingCreatedDomainEvent>(@event, new EventMetadata(0, 0));

        // Act
        using (var scope = Fixture.ServiceProvider.CreateScope())
        {
            var projections = scope.ServiceProvider.GetServices<IProjectionProcessor>();

            foreach (var projection in projections)
            {
                await projection.ProcessEventAsync(streamEvent);
            }
        }

        // Assert
        var readModel = await Fixture.ExecuteReadContextAsync(db =>
            db.Booking.AsQueryable().SingleOrDefaultAsync(x => x.BookId == bookingId));

        readModel.Should().NotBeNull();
        readModel!.PassengerInfo.Name.Should().Be("Sam");
        readModel.Trip.FlightNumber.Should().Be("BD467");
        readModel.Trip.SeatNumber.Should().Be("12A");
        readModel.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task should_not_duplicate_read_model_when_event_is_replayed()
    {
        // Arrange
        var bookingId = NewId.NextGuid();
        var @event = new BookingCreatedDomainEvent(
            PassengerInfo.Of("Sam"),
            Trip.Of("BD468", NewId.NextGuid(), NewId.NextGuid(), NewId.NextGuid(), DateTime.Now, 120m, "desc", "12B"))
        {
            Id = bookingId
        };

        var streamEvent = new StreamEvent<BookingCreatedDomainEvent>(@event, new EventMetadata(0, 0));

        // Act
        using (var scope = Fixture.ServiceProvider.CreateScope())
        {
            var projection = scope.ServiceProvider.GetServices<IProjectionProcessor>().Single(x => x is global::Booking.BookingProjection);

            await projection.ProcessEventAsync(streamEvent);
            await projection.ProcessEventAsync(streamEvent);
        }

        // Assert
        var count = await Fixture.ExecuteReadContextAsync(db =>
            db.Booking.AsQueryable().CountAsync(x => x.BookId == bookingId));

        count.Should().Be(1);
    }
}
