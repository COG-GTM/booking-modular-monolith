using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.EFCore;

public class AppDbContextBaseTests
{
    private readonly string _databaseName = DbContextFactory.NewDatabaseName();
    private readonly ICurrentUserProvider _currentUserProvider = Substitute.For<ICurrentUserProvider>();

    public AppDbContextBaseTests()
    {
        _currentUserProvider.GetCurrentUserId().Returns(42);
    }

    private FakeAppDbContext CreateContext() =>
        DbContextFactory.CreateFakeAppDbContext(_databaseName, _currentUserProvider);

    [Fact]
    public async Task save_changes_should_stamp_created_audit_fields_on_added_aggregate()
    {
        await using var context = CreateContext();
        var aggregate = FakeAggregate.Create("new");
        var before = DateTime.Now;

        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();

        aggregate.CreatedBy.Should().Be(42);
        aggregate.CreatedAt.Should().NotBeNull().And.BeOnOrAfter(before);
        aggregate.LastModified.Should().BeNull();
        aggregate.LastModifiedBy.Should().BeNull();
        aggregate.Version.Should().Be(0);
        aggregate.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task save_changes_should_stamp_modified_audit_fields_and_bump_version_on_modified_aggregate()
    {
        await using var context = CreateContext();
        var aggregate = FakeAggregate.Create("original");
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();
        _currentUserProvider.GetCurrentUserId().Returns(7);

        aggregate.Name = "changed";
        await context.SaveChangesAsync();

        aggregate.LastModifiedBy.Should().Be(7);
        aggregate.LastModified.Should().NotBeNull();
        aggregate.Version.Should().Be(1);
        aggregate.CreatedBy.Should().Be(42);
    }

    [Fact]
    public async Task save_changes_should_soft_delete_instead_of_removing_deleted_aggregate()
    {
        await using var context = CreateContext();
        var aggregate = FakeAggregate.Create("to-delete");
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();

        context.Aggregates.Remove(aggregate);
        await context.SaveChangesAsync();

        aggregate.IsDeleted.Should().BeTrue();
        aggregate.Version.Should().Be(1);
        aggregate.LastModifiedBy.Should().Be(42);
        aggregate.LastModified.Should().NotBeNull();
        context.Entry(aggregate).State.Should().Be(EntityState.Unchanged);

        await using var verificationContext = CreateContext();
        var stored = await verificationContext.Aggregates.SingleAsync(x => x.Id == aggregate.Id);
        stored.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task save_changes_without_current_user_provider_should_use_zero_as_user_id()
    {
        await using var context = DbContextFactory.CreateFakeAppDbContext(_databaseName);
        var aggregate = FakeAggregate.Create("anonymous");

        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();

        aggregate.CreatedBy.Should().Be(0);
    }

    [Fact]
    public async Task get_domain_events_should_return_events_of_tracked_aggregates_and_clear_them()
    {
        await using var context = CreateContext();
        var first = FakeAggregate.Create("first");
        var second = FakeAggregate.Create("second");
        var untouched = FakeAggregate.Create("untouched");
        context.Aggregates.AddRange(first, second, untouched);
        first.Rename("first-renamed");
        second.Rename("second-renamed");
        second.Rename("second-renamed-again");

        var events = context.GetDomainEvents();

        events.Should().HaveCount(3);
        events
            .OfType<FakeDomainEvent>()
            .Select(e => e.Name)
            .Should()
            .BeEquivalentTo("first-renamed", "second-renamed", "second-renamed-again");
        first.DomainEvents.Should().BeEmpty();
        second.DomainEvents.Should().BeEmpty();
        context.GetDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public async Task save_changes_with_concurrency_conflict_should_refresh_original_values_and_retry()
    {
        await using var seedContext = CreateContext();
        var seeded = FakeAggregate.Create("seed");
        seedContext.Aggregates.Add(seeded);
        await seedContext.SaveChangesAsync();

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstCopy = await firstContext.Aggregates.SingleAsync(x => x.Id == seeded.Id);
        var secondCopy = await secondContext.Aggregates.SingleAsync(x => x.Id == seeded.Id);

        secondCopy.Name = "second-writer";
        await secondContext.SaveChangesAsync();

        firstCopy.Name = "first-writer";
        var affected = await firstContext.SaveChangesAsync();

        affected.Should().Be(1);
        await using var verificationContext = CreateContext();
        var stored = await verificationContext.Aggregates.SingleAsync(x => x.Id == seeded.Id);
        stored.Name.Should().Be("first-writer");
        stored.Version.Should().Be(1);
    }

    [Fact]
    public async Task save_changes_when_row_was_deleted_by_another_writer_should_rethrow_concurrency_exception()
    {
        await using var seedContext = CreateContext();
        var seeded = FakeAggregate.Create("seed");
        seedContext.Aggregates.Add(seeded);
        await seedContext.SaveChangesAsync();

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstCopy = await firstContext.Aggregates.SingleAsync(x => x.Id == seeded.Id);
        var secondCopy = await secondContext.Aggregates.SingleAsync(x => x.Id == seeded.Id);

        // The synchronous SaveChanges is not intercepted by AppDbContextBase, so this is a hard delete.
        secondContext.Aggregates.Remove(secondCopy);
        secondContext.SaveChanges();

        firstCopy.Name = "first-writer";
        var act = () => firstContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
