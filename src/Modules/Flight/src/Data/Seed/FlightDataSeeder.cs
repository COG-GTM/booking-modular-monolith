using BuildingBlocks.EFCore;
using Flight.Aircrafts.Models;
using Flight.Airports.Models;
using Flight.Flights.Models;
using Flight.Seats.Models;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Flight.Data.Seed;

public class FlightDataSeeder(
    FlightDbContext flightDbContext,
    FlightReadDbContext flightReadDbContext,
    IMapper mapper
) : IDataSeeder
{
    public async Task SeedAllAsync()
    {
        var pendingMigrations = await flightDbContext.Database.GetPendingMigrationsAsync();

        if (!pendingMigrations.Any())
        {
            await SeedAirportAsync();
            await SeedAircraftAsync();
            await SeedFlightAsync();
            await SeedSeatAsync();
        }
    }

    private async Task SeedAirportAsync()
    {
        if (!await EntityFrameworkQueryableExtensions.AnyAsync(flightDbContext.Airports))
        {
            await flightDbContext.Airports.AddRangeAsync(InitialData.Airports);
            await flightDbContext.SaveChangesAsync();

            if (!await MongoQueryable.AnyAsync(flightReadDbContext.Airport.AsQueryable()))
            {
                await flightReadDbContext.Airport.InsertManyAsync(mapper.Map<List<AirportReadModel>>(InitialData.Airports));
            }
        }
    }

    private async Task SeedAircraftAsync()
    {
        if (!await EntityFrameworkQueryableExtensions.AnyAsync(flightDbContext.Aircraft))
        {
            await flightDbContext.Aircraft.AddRangeAsync(InitialData.Aircrafts);
            await flightDbContext.SaveChangesAsync();

            if (!await MongoQueryable.AnyAsync(flightReadDbContext.Aircraft.AsQueryable()))
            {
                await flightReadDbContext.Aircraft.InsertManyAsync(mapper.Map<List<AircraftReadModel>>(InitialData.Aircrafts));
            }
        }
    }


    private async Task SeedSeatAsync()
    {
        if (!await EntityFrameworkQueryableExtensions.AnyAsync(flightDbContext.Seats))
        {
            await flightDbContext.Seats.AddRangeAsync(InitialData.Seats);
            await flightDbContext.SaveChangesAsync();

            if (!await MongoQueryable.AnyAsync(flightReadDbContext.Seat.AsQueryable()))
            {
                await flightReadDbContext.Seat.InsertManyAsync(mapper.Map<List<SeatReadModel>>(InitialData.Seats));
            }
        }
        else
        {
            await RepairLegacySeatNumbersAsync();
        }
    }

    // Seat read models seeded before SeatNumber was mapped to its raw value hold "SeatNumber { Value = 12A }",
    // which GetAvailableSeats returns and ReserveSeat then rejects.
    private async Task RepairLegacySeatNumbersAsync()
    {
        var legacy = await flightReadDbContext.Seat
            .Find(Builders<SeatReadModel>.Filter.Regex(s => s.SeatNumber, new BsonRegularExpression($"^{Regex.Escape(LegacySeatNumberPrefix)}")))
            .ToListAsync();

        foreach (var seat in legacy)
        {
            if (TryParseLegacySeatNumber(seat.SeatNumber, out var seatNumber))
            {
                await flightReadDbContext.Seat.UpdateOneAsync(
                    s => s.Id == seat.Id,
                    Builders<SeatReadModel>.Update.Set(s => s.SeatNumber, seatNumber));
            }
        }
    }

    private const string LegacySeatNumberPrefix = "SeatNumber { Value = ";

    public static bool TryParseLegacySeatNumber(string value, out string seatNumber)
    {
        seatNumber = value;

        if (!value.StartsWith(LegacySeatNumberPrefix, StringComparison.Ordinal) || !value.EndsWith(" }", StringComparison.Ordinal))
            return false;

        var raw = value[LegacySeatNumberPrefix.Length..^2];
        if (raw.Length == 0)
            return false;

        seatNumber = raw;
        return true;
    }

    private async Task SeedFlightAsync()
    {
        if (!await EntityFrameworkQueryableExtensions.AnyAsync(flightDbContext.Flights))
        {
            await flightDbContext.Flights.AddRangeAsync(InitialData.Flights);
            await flightDbContext.SaveChangesAsync();

            if (!await MongoQueryable.AnyAsync(flightReadDbContext.Flight.AsQueryable()))
            {
                await flightReadDbContext.Flight.InsertManyAsync(mapper.Map<List<FlightReadModel>>(InitialData.Flights));
            }
        }
    }
}