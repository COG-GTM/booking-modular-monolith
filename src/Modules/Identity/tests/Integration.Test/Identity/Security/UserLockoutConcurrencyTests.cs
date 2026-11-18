using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Security;

public class UserLockoutConcurrencyTests : IdentityIntegrationTestBase
{
    private const string UserName = "samh";

    public UserLockoutConcurrencyTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task stale_failed_attempt_should_report_concurrency_failure_instead_of_overwriting_newer_count()
    {
        using var scopeA = Fixture.ServiceProvider.CreateScope();
        using var scopeB = Fixture.ServiceProvider.CreateScope();
        var userManagerA = scopeA.ServiceProvider.GetRequiredService<UserManager<User>>();
        var userManagerB = scopeB.ServiceProvider.GetRequiredService<UserManager<User>>();

        var userA = await userManagerA.FindByNameAsync(UserName);
        var staleUserB = await userManagerB.FindByNameAsync(UserName);

        (await userManagerA.AccessFailedAsync(userA!)).Succeeded.Should().BeTrue();
        (await userManagerA.AccessFailedAsync(userA!)).Succeeded.Should().BeTrue();

        var staleResult = await userManagerB.AccessFailedAsync(staleUserB!);

        staleResult.Succeeded.Should().BeFalse();
        staleResult.Errors.Select(e => e.Code).Should().Contain(nameof(IdentityErrorDescriber.ConcurrencyFailure));

        using var verifyScope = Fixture.ServiceProvider.CreateScope();
        var verifyUserManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var persisted = await verifyUserManager.FindByNameAsync(UserName);
        (await verifyUserManager.GetAccessFailedCountAsync(persisted!)).Should().Be(2);
    }

    [Fact]
    public async Task stale_save_should_not_clear_lockout_set_by_concurrent_request()
    {
        using var scopeA = Fixture.ServiceProvider.CreateScope();
        using var scopeB = Fixture.ServiceProvider.CreateScope();
        var userManagerA = scopeA.ServiceProvider.GetRequiredService<UserManager<User>>();
        var userManagerB = scopeB.ServiceProvider.GetRequiredService<UserManager<User>>();

        var userA = await userManagerA.FindByNameAsync(UserName);
        (await userManagerA.AccessFailedAsync(userA!)).Succeeded.Should().BeTrue();

        // Loaded with a non-zero failed count so the stale reset below really writes the row.
        var staleUserB = await userManagerB.FindByNameAsync(UserName);

        for (var attempt = 1; attempt < userManagerA.Options.Lockout.MaxFailedAccessAttempts; attempt++)
        {
            (await userManagerA.AccessFailedAsync(userA!)).Succeeded.Should().BeTrue();
        }

        (await userManagerA.IsLockedOutAsync(userA!)).Should().BeTrue();

        var staleResult = await userManagerB.ResetAccessFailedCountAsync(staleUserB!);

        staleResult.Succeeded.Should().BeFalse();
        staleResult.Errors.Select(e => e.Code).Should().Contain(nameof(IdentityErrorDescriber.ConcurrencyFailure));

        using var verifyScope = Fixture.ServiceProvider.CreateScope();
        var verifyUserManager = verifyScope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var persisted = await verifyUserManager.FindByNameAsync(UserName);
        (await verifyUserManager.IsLockedOutAsync(persisted!)).Should().BeTrue();
    }
}
