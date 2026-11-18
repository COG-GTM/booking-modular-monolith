using FluentAssertions;
using Identity.Data;
using Identity.Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityContextConcurrencyTests
{
    [Fact]
    public async Task save_should_reject_a_stale_user_update_without_overwriting_the_committed_value()
    {
        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var writer = new IdentityContext(options, NullLogger<IdentityContext>.Instance);
        await using var staleWriter = new IdentityContext(options, NullLogger<IdentityContext>.Instance);
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "traveler",
            FirstName = "Test",
            LastName = "Traveler",
            PassPortNumber = "123456",
        };
        writer.Users.Add(user);
        await writer.SaveChangesAsync();

        var staleUser = await staleWriter.Users.SingleAsync();
        user.Email = "first@example.test";
        staleUser.Email = "second@example.test";
        await writer.SaveChangesAsync();

        var act = () => staleWriter.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();

        var savedUser = await writer.Users.AsNoTracking().SingleAsync();
        savedUser.Email.Should().Be("first@example.test");
    }
}
