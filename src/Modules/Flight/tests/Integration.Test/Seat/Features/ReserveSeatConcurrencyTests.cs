using Api;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Seat.Features;

using global::Flight.Seats.Features.ReservingSeat.V1;

public class ReserveSeatConcurrencyTests : FlightIntegrationTestBase
{
    public ReserveSeatConcurrencyTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task only_one_reservation_should_commit_when_two_requests_read_the_same_available_seat()
    {
        var flight = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flight);
        var seat = new FakeCreateSeatCommand(flight.Id).Generate();
        await Fixture.SendAsync(seat);

        await using var firstScope = Fixture.ServiceProvider.CreateAsyncScope();
        await using var secondScope = Fixture.ServiceProvider.CreateAsyncScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<FlightDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<FlightDbContext>();
        var command = new ReserveSeat(seat.FlightId, seat.SeatNumber);

        // Both handlers read the original version before either context saves.
        await new ReserveSeatCommandHandler(firstContext).Handle(command, CancellationToken.None);
        await new ReserveSeatCommandHandler(secondContext).Handle(command, CancellationToken.None);

        await firstContext.SaveChangesAsync();
        var secondCommit = () => secondContext.SaveChangesAsync();
        await secondCommit.Should().ThrowAsync<DbUpdateConcurrencyException>();

        await using var verificationScope = Fixture.ServiceProvider.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<FlightDbContext>();
        var persistedSeat = await verificationContext.Seats.IgnoreQueryFilters().SingleAsync(x => x.Id == seat.Id);
        persistedSeat.IsDeleted.Should().BeTrue();
        persistedSeat.Version.Should().Be(1);
    }
}
