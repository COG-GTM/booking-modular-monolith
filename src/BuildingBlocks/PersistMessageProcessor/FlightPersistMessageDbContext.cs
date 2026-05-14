using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.PersistMessageProcessor;

public class FlightPersistMessageDbContext : PersistMessageDbContext
{
    public FlightPersistMessageDbContext(
        DbContextOptions<FlightPersistMessageDbContext> options,
        ILogger<PersistMessageDbContext>? logger = null
    )
        : base(CreateBaseOptions(options), logger) { }

    private static DbContextOptions<PersistMessageDbContext> CreateBaseOptions(
        DbContextOptions<FlightPersistMessageDbContext> options
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
