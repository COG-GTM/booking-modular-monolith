using FluentAssertions;
using Identity.Data;
using Identity.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Test.Identity.Security;

public class IdentityContextConcurrencyTests
{
    [Fact]
    public async Task stale_save_throws_and_preserves_the_first_saved_value()
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var first = new IdentityContext(options);
        first.Users.Add(
            new User
            {
                Id = Guid.NewGuid(),
                UserName = "concurrency-test",
                FirstName = "Test",
                LastName = "User",
                PassPortNumber = "TEST123",
                ConcurrencyStamp = "original",
            }
        );
        await first.SaveChangesAsync();

        await using var second = new IdentityContext(options);
        var staleUser = await second.Users.SingleAsync();
        var firstUser = await first.Users.SingleAsync();
        firstUser.ConcurrencyStamp = "first-update";
        firstUser.AccessFailedCount = 2;
        (await first.SaveChangesAsync()).Should().Be(1);

        staleUser.ConcurrencyStamp = "stale-update";
        staleUser.AccessFailedCount = 1;
        var save = () => second.SaveChangesAsync();

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        await using var verification = new IdentityContext(options);
        var persisted = await verification.Users.SingleAsync();
        persisted.ConcurrencyStamp.Should().Be("first-update");
        persisted.AccessFailedCount.Should().Be(2);
    }

    [Fact]
    public async Task saving_without_changes_returns_zero()
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var context = new IdentityContext(options);

        (await context.SaveChangesAsync()).Should().Be(0);
    }
}
