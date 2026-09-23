using BuildingBlocks.EFCore;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Fakes;

namespace Unit.Test.Common;

public static class DbContextFactory
{
    public static string NewDatabaseName() => $"bb-unit-{Guid.NewGuid():N}";

    public static DbContextOptions<TContext> Options<TContext>(string databaseName)
        where TContext : DbContext
    {
        return new DbContextOptionsBuilder<TContext>()
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .UseInMemoryDatabase(databaseName)
            .Options;
    }

    public static PersistMessageDbContext CreatePersistMessageDbContext(string? databaseName = null)
    {
        return new PersistMessageDbContext(Options<PersistMessageDbContext>(databaseName ?? NewDatabaseName()));
    }

    public static FakeAppDbContext CreateFakeAppDbContext(
        string? databaseName = null,
        ICurrentUserProvider? currentUserProvider = null
    )
    {
        return new FakeAppDbContext(
            Options<FakeAppDbContext>(databaseName ?? NewDatabaseName()),
            currentUserProvider,
            Substitute.For<ILogger<AppDbContextBase>>()
        );
    }
}
