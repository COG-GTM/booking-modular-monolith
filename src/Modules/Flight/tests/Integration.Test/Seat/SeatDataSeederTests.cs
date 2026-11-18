using BuildingBlocks.EFCore;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Data.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Seat;

public class SeatDataSeederTests : FlightIntegrationTestBase
{
    public SeatDataSeederTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task seeder_should_not_insert_seats_when_only_soft_deleted_seats_exist()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlightDbContext>();
        var seeder = scope.ServiceProvider.GetServices<IDataSeeder>().OfType<FlightDataSeeder>().Single();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        var seats = await db.Seats.ToListAsync();
        seats.Should().NotBeEmpty();
        var originalSeatIds = seats.Select(x => x.Id).ToList();
        foreach (var seat in seats)
        {
            seat.IsDeleted = true;
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        (await db.Seats.AnyAsync()).Should().BeFalse();

        var act = async () => await seeder.SeedAllAsync();

        await act.Should().NotThrowAsync();
        db.ChangeTracker.Clear();
        var persistedSeats = await db.Seats.IgnoreQueryFilters().ToListAsync();
        persistedSeats.Select(x => x.Id).Should().BeEquivalentTo(originalSeatIds);
        persistedSeats.Should().OnlyContain(x => x.IsDeleted);
        (await db.Seats.AnyAsync()).Should().BeFalse();
    }
}
