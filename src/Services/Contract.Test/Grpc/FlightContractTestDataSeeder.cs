using System.Collections.Generic;
using System.Threading.Tasks;
using BuildingBlocks.EFCore;
using Flight.Aircrafts.Models;
using Flight.Airports.Models;
using Flight.Data;
using Flight.Data.Seed;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Contract.Test.Grpc;

/// <summary>
/// Seeds the reference data (airports, aircraft) that the write-side CreateFlight command depends on via foreign keys.
/// </summary>
public class FlightContractTestDataSeeder(
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

            if (!await flightReadDbContext.Airport.AsQueryable().AnyAsync())
                await flightReadDbContext.Airport.InsertManyAsync(
                    mapper.Map<List<AirportReadModel>>(InitialData.Airports)
                );
        }

        if (!await flightDbContext.Aircraft.AnyAsync())
        {
            await flightDbContext.Aircraft.AddRangeAsync(InitialData.Aircrafts);
            await flightDbContext.SaveChangesAsync();

            if (!await flightReadDbContext.Aircraft.AsQueryable().AnyAsync())
                await flightReadDbContext.Aircraft.InsertManyAsync(
                    mapper.Map<List<AircraftReadModel>>(InitialData.Aircrafts)
                );
        }
    }
}
