using BuildingBlocks.EFCore;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using Passenger.Data;
using Passenger.Passengers.Models;
using Passenger.Passengers.ValueObjects;

namespace EndToEnd.Test;

public class PassengerTestDataSeeder(
    PassengerDbContext passengerDbContext,
    PassengerReadDbContext passengerReadDbContext,
    IMapper mapper
) : ITestDataSeeder
{
    public static readonly Guid SeededPassengerId = new("6f7b5c1e-3b8f-4a1d-9c2e-1d2f3a4b5c6d");
    public const string SeededPassportNumber = "E2E123456";

    public async Task SeedAllAsync()
    {
        await SeedPassengerAsync();
    }

    private async Task SeedPassengerAsync()
    {
        if (!await EntityFrameworkQueryableExtensions.AnyAsync(passengerDbContext.Passengers))
        {
            var passenger = global::Passenger.Passengers.Models.Passenger.Create(
                PassengerId.Of(SeededPassengerId),
                Name.Of("Seeded Passenger"),
                PassportNumber.Of(SeededPassportNumber)
            );

            await passengerDbContext.Passengers.AddAsync(passenger);
            await passengerDbContext.SaveChangesAsync();

            if (!await MongoQueryable.AnyAsync(passengerReadDbContext.Passenger.AsQueryable()))
            {
                await passengerReadDbContext.Passenger.InsertOneAsync(mapper.Map<PassengerReadModel>(passenger));
            }
        }
    }
}
