using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Unit.Test.Fakes;

namespace Unit.Test.Common;

public static class DbContextFactory
{
    public static PassengerDbContext Create()
    {
        var options = new DbContextOptionsBuilder<PassengerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new PassengerDbContext(options);

        context.Passengers.Add(
            FakePassengerCreate.Generate(
                FakePassengerCreate.SeededPassengerId,
                FakePassengerCreate.SeededPassportNumber
            )
        );
        context.SaveChanges();

        return context;
    }

    public static void Destroy(PassengerDbContext context)
    {
        context.Database.EnsureDeleted();
        context.Dispose();
    }
}
