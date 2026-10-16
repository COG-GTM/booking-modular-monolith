using BuildingBlocks.EFCore;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.Persistence;

public class AppDbContextBaseTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public AppDbContextBaseTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private PersistAppDbContext CreateContext(
        ICurrentUserProvider? currentUserProvider = null,
        Microsoft.Extensions.Logging.ILogger<AppDbContextBase>? logger = null
    )
    {
        var options = new DbContextOptionsBuilder<PersistAppDbContext>().UseSqlite(_connection).Options;
        var context = new PersistAppDbContext(options, currentUserProvider, logger);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task added_aggregate_gets_audit_fields_from_current_user()
    {
        var provider = Substitute.For<ICurrentUserProvider>();
        provider.GetCurrentUserId().Returns(42L);
        await using var context = CreateContext(provider);

        var aggregate = new PersistFlightAggregate { Id = 1, Name = "A" };
        context.Flights.Add(aggregate);
        await context.SaveChangesAsync();

        aggregate.CreatedBy.Should().Be(42);
        aggregate.CreatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task added_aggregate_gets_zero_created_by_without_user_provider()
    {
        await using var context = CreateContext();

        var aggregate = new PersistFlightAggregate { Id = 2, Name = "B" };
        context.Flights.Add(aggregate);
        await context.SaveChangesAsync();

        aggregate.CreatedBy.Should().Be(0);
        aggregate.CreatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task modified_aggregate_sets_last_modified_and_increments_version()
    {
        var provider = Substitute.For<ICurrentUserProvider>();
        provider.GetCurrentUserId().Returns(7L);
        await using var context = CreateContext(provider);

        var aggregate = new PersistFlightAggregate { Id = 3, Name = "C" };
        context.Flights.Add(aggregate);
        await context.SaveChangesAsync();
        var version = aggregate.Version;

        aggregate.Name = "C2";
        await context.SaveChangesAsync();

        aggregate.LastModifiedBy.Should().Be(7);
        aggregate.LastModified.Should().NotBeNull();
        aggregate.Version.Should().Be(version + 1);
    }

    [Fact]
    public async Task remove_soft_deletes_and_hides_row_behind_query_filter()
    {
        await using var context = CreateContext();

        var aggregate = new PersistFlightAggregate { Id = 4, Name = "D" };
        context.Flights.Add(aggregate);
        await context.SaveChangesAsync();
        var version = aggregate.Version;

        context.Flights.Remove(aggregate);
        await context.SaveChangesAsync();

        aggregate.IsDeleted.Should().BeTrue();
        aggregate.Version.Should().Be(version + 1);
        (await context.Flights.AnyAsync(e => e.Id == 4)).Should().BeFalse();
        var hidden = await context.Flights.IgnoreQueryFilters().SingleAsync(e => e.Id == 4);
        hidden.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task get_domain_events_returns_events_and_clears_them()
    {
        await using var context = CreateContext();

        var aggregate = new PersistFlightAggregate { Id = 5, Name = "E" };
        aggregate.AddDomainEvent(new PersistOrderCreated(Guid.NewGuid()));
        context.Flights.Add(aggregate);
        await context.SaveChangesAsync();

        var events = context.GetDomainEvents();

        events.Should().ContainSingle().Which.Should().BeOfType<PersistOrderCreated>();
        context.GetDomainEvents().Should().BeEmpty();
        aggregate.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task begin_and_commit_transaction_persists_changes()
    {
        await using var context = CreateContext();

        await context.BeginTransactionAsync();
        context.Flights.Add(new PersistFlightAggregate { Id = 6, Name = "F" });
        await context.CommitTransactionAsync();

        (await context.Flights.AnyAsync(e => e.Id == 6)).Should().BeTrue();
    }

    [Fact]
    public async Task second_begin_transaction_is_a_no_op()
    {
        await using var context = CreateContext();

        await context.BeginTransactionAsync();
        var first = context.Database.CurrentTransaction;
        await context.BeginTransactionAsync();

        context.Database.CurrentTransaction.Should().BeSameAs(first);

        await context.RollbackTransactionAsync();
    }

    [Fact]
    public async Task rollback_transaction_discards_changes()
    {
        await using var context = CreateContext();

        await context.BeginTransactionAsync();
        context.Flights.Add(new PersistFlightAggregate { Id = 7, Name = "G" });
        await context.SaveChangesAsync();
        await context.RollbackTransactionAsync();

        (await context.Flights.IgnoreQueryFilters().AnyAsync(e => e.Id == 7)).Should().BeFalse();
    }

    [Fact]
    public async Task commit_transaction_rolls_back_and_rethrows_when_save_fails()
    {
        var options = new DbContextOptionsBuilder<PersistUniqueNameDbContext>().UseSqlite(_connection).Options;
        await using var context = new PersistUniqueNameDbContext(options);
        context.Database.EnsureCreated();

        await context.BeginTransactionAsync();
        context.Flights.Add(new PersistFlightAggregate { Id = 8, Name = "dupe" });
        context.Flights.Add(new PersistFlightAggregate { Id = 9, Name = "dupe" });

        var act = () => context.CommitTransactionAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
        (await context.Flights.IgnoreQueryFilters().AnyAsync(e => e.Name == "dupe")).Should().BeFalse();
    }

    [Fact]
    public async Task execute_transactional_saves_changes()
    {
        await using var context = CreateContext();

        context.Flights.Add(new PersistFlightAggregate { Id = 10, Name = "J" });
        await context.ExecuteTransactionalAsync();

        (await context.Flights.AnyAsync(e => e.Id == 10)).Should().BeTrue();
    }

    [Fact]
    public async Task save_changes_resolves_concurrency_conflict_client_wins()
    {
        var options = new DbContextOptionsBuilder<PersistAppDbContext>().UseSqlite(_connection).Options;
        await using var context1 = new PersistAppDbContext(options);
        context1.Database.EnsureCreated();
        context1.Flights.Add(new PersistFlightAggregate { Id = 11, Name = "original" });
        await context1.SaveChangesAsync();
        context1.ChangeTracker.Clear();

        var winner = await context1.Flights.SingleAsync(e => e.Id == 11);
        winner.Name = "winner";

        // A second context bumps the row (and its concurrency token) before the first saves.
        await using var context2 = new PersistAppDbContext(options);
        var other = await context2.Flights.SingleAsync(e => e.Id == 11);
        other.Name = "other";
        await context2.SaveChangesAsync();

        await context1.SaveChangesAsync();

        context1.ChangeTracker.Clear();
        (await context1.Flights.SingleAsync(e => e.Id == 11)).Name.Should().Be("winner");
    }

    [Fact]
    public async Task save_changes_rethrows_concurrency_exception_when_row_deleted()
    {
        var options = new DbContextOptionsBuilder<PersistAppDbContext>().UseSqlite(_connection).Options;
        await using var context1 = new PersistAppDbContext(options, logger: NullLogger<AppDbContextBase>.Instance);
        context1.Database.EnsureCreated();
        context1.Flights.Add(new PersistFlightAggregate { Id = 12, Name = "gone" });
        await context1.SaveChangesAsync();
        context1.ChangeTracker.Clear();

        var tracked = await context1.Flights.IgnoreQueryFilters().SingleAsync(e => e.Id == 12);
        tracked.Name = "stale";

        // Remove() only soft-deletes, so the row must be physically deleted with raw SQL.
        await context1.Database.ExecuteSqlRawAsync("""DELETE FROM "Flights" WHERE "Id" = 12""");

        var act = () => context1.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
