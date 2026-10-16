using Booking;
using Booking.Data;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.Mongo;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test;

public class BookingProjectionTests
{
    private sealed record OtherNotification : INotification;

    [Fact]
    public void read_context_uses_booking_collection_and_configured_database_without_connecting()
    {
        using var context = CreateContext();

        context.Booking.CollectionNamespace.CollectionName.Should().Be("booking");
        context.Booking.CollectionNamespace.DatabaseNamespace.DatabaseName.Should().Be("booking_unit");
    }

    [Fact]
    public async Task non_booking_created_notification_completes_without_mongo_access()
    {
        using var context = CreateContext();
        var projection = new BookingProjection(context);
        var streamEvent = new StreamEvent<INotification>(new OtherNotification(), new EventMetadata(0, 0));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        await projection.ProcessEventAsync(streamEvent, timeout.Token).WaitAsync(TimeSpan.FromSeconds(2));
    }

    private static BookingReadDbContext CreateContext() =>
        new(
            Options.Create(
                new MongoOptions { ConnectionString = "mongodb://localhost:1", DatabaseName = "booking_unit" }
            )
        );
}
