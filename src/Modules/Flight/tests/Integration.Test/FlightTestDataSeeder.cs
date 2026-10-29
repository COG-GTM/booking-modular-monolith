using BuildingBlocks.EFCore;
using Flight.Aircrafts.Models;
using Flight.Airports.Models;
using Flight.Data;
using Flight.Data.Seed;
using Flight.Flights.Models;
using Flight.Seats.Models;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using MongoDB.Driver.Linq;

namespace Integration.Test;

public class FlightTestDataSeeder(
    FlightDbContext flightDbContext,
    FlightReadDbContext flightReadDbContext,
    IMapper mapper
) : ITestDataSeeder
{
    public async Task SeedAllAsync()
    {
        await SeedAirportAsync();
        await SeedAircraftAsync();
        await SeedFlightAsync();
        await SeedSeatAsync();
    }

    private async Task SeedAirportAsync()
    {
        var existingAirportIds = await EntityFrameworkQueryableExtensions.ToListAsync(
            flightDbContext.Airports.Select(airport => airport.Id)
        );
        var missingAirports = InitialData.Airports.Where(airport => !existingAirportIds.Contains(airport.Id)).ToList();
        if (missingAirports.Count > 0)
        {
            await flightDbContext.Airports.AddRangeAsync(missingAirports);
            await flightDbContext.SaveChangesAsync();
        }

        var airportReadModels = mapper.Map<List<AirportReadModel>>(InitialData.Airports);
        var existingAirportReadModelIds = await MongoQueryable.ToListAsync(
            flightReadDbContext.Airport.AsQueryable().Select(airport => airport.AirportId)
        );
        var missingAirportReadModels = airportReadModels
            .Where(airport => !existingAirportReadModelIds.Contains(airport.AirportId))
            .ToList();
        if (missingAirportReadModels.Count > 0)
        {
            await flightReadDbContext.Airport.InsertManyAsync(missingAirportReadModels);
        }
    }

    private async Task SeedAircraftAsync()
    {
        var existingAircraftIds = await EntityFrameworkQueryableExtensions.ToListAsync(
            flightDbContext.Aircraft.Select(aircraft => aircraft.Id)
        );
        var missingAircraft = InitialData.Aircrafts.Where(aircraft => !existingAircraftIds.Contains(aircraft.Id)).ToList();
        if (missingAircraft.Count > 0)
        {
            await flightDbContext.Aircraft.AddRangeAsync(missingAircraft);
            await flightDbContext.SaveChangesAsync();
        }

        var aircraftReadModels = mapper.Map<List<AircraftReadModel>>(InitialData.Aircrafts);
        var existingAircraftReadModelIds = await MongoQueryable.ToListAsync(
            flightReadDbContext.Aircraft.AsQueryable().Select(aircraft => aircraft.AircraftId)
        );
        var missingAircraftReadModels = aircraftReadModels
            .Where(aircraft => !existingAircraftReadModelIds.Contains(aircraft.AircraftId))
            .ToList();
        if (missingAircraftReadModels.Count > 0)
        {
            await flightReadDbContext.Aircraft.InsertManyAsync(missingAircraftReadModels);
        }
    }

    private async Task SeedSeatAsync()
    {
        var existingSeatIds = await EntityFrameworkQueryableExtensions.ToListAsync(
            flightDbContext.Seats.Select(seat => seat.Id)
        );
        var missingSeats = InitialData.Seats.Where(seat => !existingSeatIds.Contains(seat.Id)).ToList();
        if (missingSeats.Count > 0)
        {
            await flightDbContext.Seats.AddRangeAsync(missingSeats);
            await flightDbContext.SaveChangesAsync();
        }

        var seatReadModels = mapper.Map<List<SeatReadModel>>(InitialData.Seats);
        var existingSeatReadModelIds = await MongoQueryable.ToListAsync(
            flightReadDbContext.Seat.AsQueryable().Select(seat => seat.SeatId)
        );
        var missingSeatReadModels = seatReadModels
            .Where(seat => !existingSeatReadModelIds.Contains(seat.SeatId))
            .ToList();
        if (missingSeatReadModels.Count > 0)
        {
            await flightReadDbContext.Seat.InsertManyAsync(missingSeatReadModels);
        }
    }

    private async Task SeedFlightAsync()
    {
        var existingFlightIds = await EntityFrameworkQueryableExtensions.ToListAsync(
            flightDbContext.Flights.Select(flight => flight.Id)
        );
        var missingFlights = InitialData.Flights.Where(flight => !existingFlightIds.Contains(flight.Id)).ToList();
        if (missingFlights.Count > 0)
        {
            await flightDbContext.Flights.AddRangeAsync(missingFlights);
            await flightDbContext.SaveChangesAsync();
        }

        var flightReadModels = mapper.Map<List<FlightReadModel>>(InitialData.Flights);
        var existingFlightReadModelIds = await MongoQueryable.ToListAsync(
            flightReadDbContext.Flight.AsQueryable().Select(flight => flight.FlightId)
        );
        var missingFlightReadModels = flightReadModels
            .Where(flight => !existingFlightReadModelIds.Contains(flight.FlightId))
            .ToList();
        if (missingFlightReadModels.Count > 0)
        {
            await flightReadDbContext.Flight.InsertManyAsync(missingFlightReadModels);
        }
    }
}