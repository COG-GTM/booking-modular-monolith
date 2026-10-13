using BuildingBlocks.EFCore;
using Flight.Aircrafts.Models;
using Flight.Airports.Models;
using Flight.Data;
using Flight.Data.Seed;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;

namespace Flight.Host.Test;

// TestFixtureCore resets the write database after every test, so reference data that REST create
// flight depends on is re-seeded per test.
public class FlightHostTestDataSeeder(
    FlightDbContext flightDbContext,
    FlightReadDbContext flightReadDbContext,
    IMapper mapper
) : ITestDataSeeder
{
    public async Task SeedAllAsync()
    {
        if (!await flightDbContext.Airports.AnyAsync())
        {
            await flightDbContext.Airports.AddRangeAsync(InitialData.Airports);
            await flightDbContext.SaveChangesAsync();
            await flightReadDbContext.Airport.InsertManyAsync(mapper.Map<List<AirportReadModel>>(InitialData.Airports));
        }

        if (!await flightDbContext.Aircraft.AnyAsync())
        {
            await flightDbContext.Aircraft.AddRangeAsync(InitialData.Aircrafts);
            await flightDbContext.SaveChangesAsync();
            await flightReadDbContext.Aircraft.InsertManyAsync(mapper.Map<List<AircraftReadModel>>(InitialData.Aircrafts));
        }
    }
}
