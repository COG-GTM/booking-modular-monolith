using MassTransit;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Passenger.Passengers.ValueObjects;

namespace Unit.Test.Common;

public static class DbContextFactory
{
    private static readonly Guid _passengerId1 = NewId.NextGuid();

    public static PassengerDbContext Create()
    {
        var options = new DbContextOptionsBuilder<PassengerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new PassengerDbContext(options, currentUserProvider: null, null);

        PassengerDataSeeder(context);

        return context;
    }

    private static void PassengerDataSeeder(PassengerDbContext context)
    {
        var passengers = new List<global::Passenger.Passengers.Models.Passenger>
        {
            global::Passenger.Passengers.Models.Passenger.Create(
                PassengerId.Of(_passengerId1),
                Name.Of("John Doe"),
                PassportNumber.Of("AB123456")
            ),
        };

        context.Passengers.AddRange(passengers);
        context.SaveChanges();
    }

    public static void Destroy(PassengerDbContext context)
    {
        context.Database.EnsureDeleted();
        context.Dispose();
    }
}
