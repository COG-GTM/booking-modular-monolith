using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.PersistMessageProcessor;

public interface IPersistMessageDbContext
{
    DbSet<PersistMessage> PersistMessage { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteTransactionalAsync(CancellationToken cancellationToken = default);
}

// Outbox/inbox store owned by a single module; TModule is the module's marker type (e.g. FlightRoot).
public interface IPersistMessageDbContext<TModule> : IPersistMessageDbContext
    where TModule : class;
