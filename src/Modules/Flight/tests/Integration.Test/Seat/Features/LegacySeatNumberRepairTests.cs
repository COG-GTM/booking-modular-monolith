using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using Flight.Data.Seed;
using Flight.Seats.Models;
using FluentAssertions;
using MapsterMapper;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Xunit;

namespace Integration.Test.Seat.Features;

public class LegacySeatNumberRepairTests : FlightIntegrationTestBase
{
    public LegacySeatNumberRepairTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task startup_seeding_repairs_legacy_seat_numbers_and_leaves_other_read_models_untouched()
    {
        var flightId = NewId.NextGuid();
        var legacy = ReadModel(flightId, "SeatNumber { Value = 21C }");
        var malformed = ReadModel(flightId, "SeatNumber { Value =  }");
        var clean = ReadModel(flightId, "22D");
        await Fixture.ExecuteReadContextAsync(ctx => ctx.Seat.InsertManyAsync([legacy, malformed, clean]));

        await RunStartupSeederAsync();

        var stored = await SeatNumbersAsync(flightId);
        stored[legacy.Id].Should().Be("21C");
        stored[malformed.Id].Should().Be("SeatNumber { Value =  }");
        stored[clean.Id].Should().Be("22D");
    }

    [Fact]
    public async Task repaired_seats_are_returned_by_grpc_and_can_be_reserved()
    {
        var flightId = NewId.NextGuid();
        var legacy = ReadModel(flightId, "SeatNumber { Value = 23A }");
        await Fixture.ExecuteReadContextAsync(ctx => ctx.Seat.InsertOneAsync(legacy));

        await RunStartupSeederAsync();
        await RunStartupSeederAsync();

        var client = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);
        var seats = await client.GetAvailableSeatsAsync(
            new GetAvailableSeatsRequest { FlightId = flightId.ToString() }
        );

        seats.SeatDtos.Select(s => s.SeatNumber).Should().Equal("23A");
        (await SeatNumbersAsync(flightId))[legacy.Id].Should().Be("23A");
    }

    private async Task RunStartupSeederAsync()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var seeder = new FlightDataSeeder(
            scope.ServiceProvider.GetRequiredService<FlightDbContext>(),
            scope.ServiceProvider.GetRequiredService<FlightReadDbContext>(),
            scope.ServiceProvider.GetRequiredService<IMapper>()
        );

        await seeder.SeedAllAsync();
    }

    private Task<Dictionary<Guid, string>> SeatNumbersAsync(Guid flightId) =>
        Fixture.ExecuteReadContextAsync(async ctx =>
            (await ctx.Seat.Find(s => s.FlightId == flightId).ToListAsync()).ToDictionary(s => s.Id, s => s.SeatNumber)
        );

    private static SeatReadModel ReadModel(Guid flightId, string seatNumber) =>
        new()
        {
            Id = NewId.NextGuid(),
            SeatId = NewId.NextGuid(),
            SeatNumber = seatNumber,
            Type = global::Flight.Seats.Enums.SeatType.Window,
            Class = global::Flight.Seats.Enums.SeatClass.Economy,
            FlightId = flightId,
            IsDeleted = false,
        };
}
