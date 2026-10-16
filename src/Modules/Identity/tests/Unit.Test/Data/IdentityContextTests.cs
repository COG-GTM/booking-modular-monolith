namespace Unit.Test.Data;

using FluentAssertions;
using global::Identity.Data;
using global::Identity.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

public class IdentityContextTests
{
    private static IdentityContext CreateContext(string databaseName)
    {
        var options = new DbContextOptionsBuilder<IdentityContext>().UseInMemoryDatabase(databaseName).Options;

        return new IdentityContext(options);
    }

    private static User CreateUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            UserName = "context-user",
            FirstName = "Ctx",
            LastName = "User",
            PassPortNumber = "P0001",
            Email = "ctx@test.com",
        };

    [Fact]
    public async Task should_keep_version_at_zero_for_newly_added_user()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var user = CreateUser();

        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.Version.Should().Be(0);
    }

    [Fact]
    public async Task should_increment_version_when_user_is_modified()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var user = CreateUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.Email = "updated@test.com";
        await context.SaveChangesAsync();

        user.Version.Should().Be(1);
    }

    [Fact]
    public async Task should_increment_version_before_deleting_user()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var user = CreateUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        user.Version.Should().Be(1);
        (await context.Users.FindAsync(user.Id)).Should().BeNull();
    }

    [Fact]
    public async Task should_return_empty_domain_events_for_identity_entities()
    {
        await using var context = CreateContext(Guid.NewGuid().ToString());
        var user = CreateUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.GetDomainEvents().Should().BeEmpty();
    }
}
