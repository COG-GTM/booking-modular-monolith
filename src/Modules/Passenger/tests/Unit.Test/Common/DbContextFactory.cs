using Microsoft.EntityFrameworkCore;
using Passenger.Data;

namespace Unit.Test.Common;

public static class DbContextFactory
{
    public static PassengerDbContext Create()
    {
        var options = new DbContextOptionsBuilder<PassengerDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PassengerDbContext(options, null, null);
    }
}
