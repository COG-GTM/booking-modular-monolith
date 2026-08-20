using System.Linq;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.EFCore;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Features;

public class IdentityDataSeederTests : IdentityIntegrationTestBase
{
    public IdentityDataSeederTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_not_seed_users_when_seed_passwords_are_not_configured()
    {
        // Arrange
        await Fixture.ExecuteDbContextAsync(db =>
        {
            db.Users.RemoveRange(db.Users);
            return db.SaveChangesAsync();
        });

        using var scope = Fixture.ServiceProvider.CreateScope();
        var seeder = scope.ServiceProvider.GetServices<IDataSeeder>().OfType<IdentityDataSeeder>().Single();

        // Act
        await seeder.SeedAllAsync();

        // Assert
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        (await userManager.FindByNameAsync("samh")).Should().BeNull();
        (await userManager.FindByNameAsync("meysamh2")).Should().BeNull();
    }
}
