using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.PersistMessageProcessor;

public interface IPersistMessageDbContext
{
    DbSet<PersistMessage> PersistMessage { get; }
    DbSet<InboxMessage> InboxMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task ExecuteTransactionalAsync(CancellationToken cancellationToken = default);
}
