using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.PersistMessageProcessor;

public class BookingPersistMessageDbContext : PersistMessageDbContext
{
    public BookingPersistMessageDbContext(
        DbContextOptions<BookingPersistMessageDbContext> options,
        ILogger<PersistMessageDbContext>? logger = null
    )
        : base(CreateBaseOptions(options), logger) { }

    private static DbContextOptions<PersistMessageDbContext> CreateBaseOptions(
        DbContextOptions<BookingPersistMessageDbContext> options
    )
    {
        var builder = new DbContextOptionsBuilder<PersistMessageDbContext>();
        foreach (var extension in options.Extensions)
        {
            ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
        }
        return builder.Options;
    }
}
