using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.PersistMessageProcessor;

public sealed record ModulePersistenceRegistration(string ModuleName, Type ModuleType)
{
    public IPersistMessageProcessor ResolveProcessor(IServiceProvider services) =>
        (IPersistMessageProcessor)
            services.GetRequiredService(typeof(IPersistMessageProcessor<>).MakeGenericType(ModuleType));

    public DbContext ResolveDbContext(IServiceProvider services) =>
        (DbContext)services.GetRequiredService(typeof(PersistMessageDbContext<>).MakeGenericType(ModuleType));
}
