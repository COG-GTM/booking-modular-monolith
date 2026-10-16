using BuildingBlocks.Core.Event;
using BuildingBlocks.Core.Model;
using BuildingBlocks.EFCore;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Unit.Test.Persistence;

// Shared doubles for the persistence/messaging tests. All message/event types are top-level with
// unique names: TypeProvider matches by short Name across the AppDomain and EventTypeMapper maps
// '.' <-> '_', so names must never collide or contain underscores.

public sealed record PersistFlightAggregate : Aggregate<long>
{
    public string Name { get; set; } = string.Empty;
}

public sealed record PersistOrderCreated(Guid OrderId) : IDomainEvent;

public sealed record PersistItemCreatedIntegrationEvent(Guid ItemId) : IIntegrationEvent;

public sealed record PersistCreateReadModel(Guid Id) : IInternalCommand;

public sealed record PersistPlainPayload(string Value);

public sealed class PersistFlightModule;

public sealed class PersistPassengerModule;

public sealed class PersistAppDbContext(
    DbContextOptions<PersistAppDbContext> options,
    BuildingBlocks.Web.ICurrentUserProvider? currentUserProvider = null,
    Microsoft.Extensions.Logging.ILogger<AppDbContextBase>? logger = null
) : AppDbContextBase(options, currentUserProvider, logger)
{
    public DbSet<PersistFlightAggregate> Flights => Set<PersistFlightAggregate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<PersistFlightAggregate>(entity =>
        {
            entity.Property(e => e.Version).IsConcurrencyToken();
            entity.Property(e => e.Name).HasMaxLength(50);
        });
        builder.FilterSoftDeletedProperties();
    }
}

public sealed class PersistUniqueNameDbContext(DbContextOptions<PersistUniqueNameDbContext> options)
    : AppDbContextBase(options)
{
    public DbSet<PersistFlightAggregate> Flights => Set<PersistFlightAggregate>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<PersistFlightAggregate>(entity =>
        {
            entity.HasIndex(e => e.Name).IsUnique();
        });
    }
}

public sealed class PersistSnakeParent
{
    public int ParentId { get; set; }
    public List<PersistSnakeChild> Children { get; } = [];
}

public sealed class PersistSnakeChild
{
    public int ChildId { get; set; }
    public string ChildName { get; set; } = string.Empty;
    public PersistSnakeParent? Parent { get; set; }
}

public sealed class PersistSnakeCaseDbContext(DbContextOptions<PersistSnakeCaseDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<PersistSnakeParent>(e =>
        {
            e.HasKey(x => x.ParentId);
            e.HasMany(x => x.Children).WithOne(x => x.Parent);
        });
        builder.Entity<PersistSnakeChild>(e => e.HasKey(x => x.ChildId));
        builder.ToSnakeCaseTables();
    }
}

public sealed class PersistTestSeeder : IDataSeeder
{
    public int Calls { get; private set; }

    public Task SeedAllAsync()
    {
        Calls++;
        return Task.CompletedTask;
    }
}

public sealed class PersistTestDataSeeder : ITestDataSeeder
{
    public int Calls { get; private set; }

    public Task SeedAllAsync()
    {
        Calls++;
        return Task.CompletedTask;
    }
}

public sealed class PersistMongoDbContext(IOptions<MongoOptions> options) : MongoDbContext(options);

public sealed class PersistMongoDbContextB(IOptions<MongoOptions> options) : MongoDbContext(options);

public interface IPersistMongoServiceContext : IMongoDbContext;

public sealed class PersistMongoServiceContext(IOptions<MongoOptions> options)
    : MongoDbContext(options),
        IPersistMongoServiceContext;

public sealed record PersistMongoFlight : Aggregate<Guid>
{
    public string Name { get; set; } = string.Empty;
}

public sealed class PersistImmutablePoco
{
    public PersistImmutablePoco(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; }
    public string Name { get; }
}

public sealed class PersistMismatchedCtorPoco
{
    public PersistMismatchedCtorPoco(string other)
    {
        Name = other;
    }

    public Guid Id { get; }
    public string Name { get; }
}

public sealed record PersistPing(Guid Id) : IIntegrationEvent;

public sealed class PersistTestConsumer : IConsumer<PersistPing>
{
    public Task Consume(ConsumeContext<PersistPing> context) => Task.CompletedTask;
}

// EventStoreDB doubles (top-level, underscore-free names so EventTypeMapper round-trips them).
public sealed record EsdbItemCreated(Guid AggregateId, string Name) : IDomainEvent;

public sealed record EsdbItemRenamed(Guid AggregateId, string Name) : IDomainEvent;

public sealed record EsdbCustomMappedEvent(Guid Id) : IDomainEvent;

public sealed record EsdbPrivateSetEvent : IDomainEvent
{
    public EsdbPrivateSetEvent(Guid id, string name)
    {
        Id = id;
        Name = name;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
}

public sealed record EsdbTestAggregate : AggregateEventSourcing<Guid>
{
    public string Name { get; private set; } = string.Empty;

    public void Create(Guid id, string name)
    {
        var @event = new EsdbItemCreated(id, name);
        AddDomainEvent(@event);
        When(@event);
    }

    public void Rename(string name)
    {
        var @event = new EsdbItemRenamed(Id, name);
        AddDomainEvent(@event);
        When(@event);
    }

    public override void When(object @event)
    {
        switch (@event)
        {
            case EsdbItemCreated created:
                Id = created.AggregateId;
                Name = created.Name;
                break;
            case EsdbItemRenamed renamed:
                Name = renamed.Name;
                break;
        }
    }
}

public sealed class EsdbRecordingProjection : IProjectionProcessor
{
    public List<StreamEvent> Events { get; } = [];

    public Task ProcessEventAsync<T>(StreamEvent<T> streamEvent, CancellationToken cancellationToken = default)
        where T : INotification
    {
        Events.Add(streamEvent);
        return Task.CompletedTask;
    }
}
