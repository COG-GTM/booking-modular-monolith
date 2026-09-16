using Identity.Data;
using Microsoft.EntityFrameworkCore;

namespace Unit.Test.Common;

public static class DbContextFactory
{
    public static IdentityContext Create()
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var context = new IdentityContext(options);
        context.Database.EnsureCreated();

        return context;
    }

    public static void Destroy(IdentityContext context)
    {
        context.Database.EnsureDeleted();
        context.Dispose();
    }
}
