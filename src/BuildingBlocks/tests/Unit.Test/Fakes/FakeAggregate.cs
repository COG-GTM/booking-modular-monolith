using BuildingBlocks.Core.Model;
using BuildingBlocks.EFCore;
using BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Unit.Test.Fakes;

public record FakeAggregate : Aggregate<Guid>
{
    public string Name { get; set; } = string.Empty;

    public static FakeAggregate Create(string name)
    {
        return new FakeAggregate { Id = Guid.NewGuid(), Name = name };
    }

    public void Rename(string name)
    {
        Name = name;
        AddDomainEvent(new FakeDomainEvent(Guid.NewGuid(), name));
    }
}

public sealed class FakeAppDbContext : AppDbContextBase
{
    public FakeAppDbContext(
        DbContextOptions<FakeAppDbContext> options,
        ICurrentUserProvider? currentUserProvider = null,
        ILogger<AppDbContextBase>? logger = null
    )
        : base(options, currentUserProvider, logger) { }

    public DbSet<FakeAggregate> Aggregates => Set<FakeAggregate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<FakeAggregate>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Version).IsConcurrencyToken();
            b.Ignore(x => x.DomainEvents);
        });
    }
}
