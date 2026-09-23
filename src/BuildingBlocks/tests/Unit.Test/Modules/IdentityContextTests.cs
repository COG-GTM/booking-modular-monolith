using FluentAssertions;
using Identity.Data;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Modules;

public class IdentityContextTests
{
    private readonly string _databaseName = DbContextFactory.NewDatabaseName();

    private IdentityContext CreateContext() => ModuleFakes.CreateIdentityContext(_databaseName);

    private async Task<Guid> SeedUserAsync()
    {
        await using var context = CreateContext();
        var user = ModuleFakes.CreateUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task save_changes_should_not_bump_version_for_added_user()
    {
        await using var context = CreateContext();
        var user = ModuleFakes.CreateUser();
        context.Users.Add(user);

        await context.SaveChangesAsync();

        user.Version.Should().Be(0);
    }

    [Fact]
    public async Task save_changes_should_increment_version_for_modified_user()
    {
        var id = await SeedUserAsync();

        await using var context = CreateContext();
        var user = await context.Users.SingleAsync(x => x.Id == id);
        user.PhoneNumber = "+1-555-0100";
        await context.SaveChangesAsync();

        user.Version.Should().Be(1);
        await using var verification = CreateContext();
        (await verification.Users.SingleAsync(x => x.Id == id)).Version.Should().Be(1);
    }

    [Fact]
    public async Task save_changes_should_increment_version_for_deleted_user_and_remove_it()
    {
        var id = await SeedUserAsync();

        await using var context = CreateContext();
        var user = await context.Users.SingleAsync(x => x.Id == id);
        context.Users.Remove(user);
        await context.SaveChangesAsync();

        user.Version.Should().Be(1);
        await using var verification = CreateContext();
        (await verification.Users.AnyAsync(x => x.Id == id)).Should().BeFalse();
    }

    [Fact]
    public async Task save_changes_with_concurrency_conflict_should_refresh_original_values_and_retry()
    {
        var id = await SeedUserAsync();

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstCopy = await firstContext.Users.SingleAsync(x => x.Id == id);
        var secondCopy = await secondContext.Users.SingleAsync(x => x.Id == id);

        secondCopy.PhoneNumber = "second-writer";
        await secondContext.SaveChangesAsync();

        firstCopy.PhoneNumber = "first-writer";
        var affected = await firstContext.SaveChangesAsync();

        affected.Should().Be(1);
        await using var verification = CreateContext();
        var stored = await verification.Users.SingleAsync(x => x.Id == id);
        stored.PhoneNumber.Should().Be("first-writer");
        stored.Version.Should().Be(1);
    }

    [Fact]
    public async Task save_changes_when_user_was_deleted_by_another_writer_should_rethrow_concurrency_exception()
    {
        var id = await SeedUserAsync();

        await using var firstContext = CreateContext();
        await using var secondContext = CreateContext();
        var firstCopy = await firstContext.Users.SingleAsync(x => x.Id == id);
        var secondCopy = await secondContext.Users.SingleAsync(x => x.Id == id);

        secondContext.Users.Remove(secondCopy);
        await secondContext.SaveChangesAsync();

        firstCopy.PhoneNumber = "first-writer";
        var act = () => firstContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [Fact]
    public async Task get_domain_events_should_be_empty_because_identity_tracks_no_aggregates()
    {
        await using var context = CreateContext();
        context.Users.Add(ModuleFakes.CreateUser());

        context.GetDomainEvents().Should().BeEmpty();
    }
}
