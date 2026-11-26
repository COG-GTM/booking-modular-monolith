using System.Reflection;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using FluentAssertions;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Integration.Test.Identity.Seed;

[Collection("Identity seed data")]
public class IdentityDataSeederBootstrapTests
{
    private const string AdminPassword = "Configured-Admin-Pw1";
    private const string UserPassword = "Configured-User-Pw1";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_not_seed_missing_user_when_the_database_already_has_users(bool adminFirst)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var events = Substitute.For<IEventDispatcher>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<IdentityContext>(options =>
            options.UseSqlite(connection).ReplaceService<IMigrationsAssembly, NoMigrationsAssembly>()
        );
        services.AddIdentity<User, Role>().AddEntityFrameworkStores<IdentityContext>();
        services.AddSingleton(events);
        await using var provider = services.BuildServiceProvider();

        var firstUserName = adminFirst ? "samh" : "samh2";
        var missingUserName = adminFirst ? "samh2" : "samh";
        Guid firstId;
        await using (var scope = provider.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IdentityContext>();
            await context.Database.EnsureCreatedAsync();
            var options = new IdentitySeedOptions
            {
                AdminPassword = adminFirst ? AdminPassword : null,
                UserPassword = adminFirst ? null : UserPassword,
            };
            await SeedAsync(scope.ServiceProvider, options);
            var firstUser = await context.Users.SingleAsync();
            firstUser.UserName.Should().Be(firstUserName);
            firstId = firstUser.Id;
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            await SeedAsync(
                scope.ServiceProvider,
                new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
            );

            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            (await users.FindByNameAsync(missingUserName)).Should().BeNull();

            var firstUser = await users.Users.SingleAsync();
            firstUser.UserName.Should().Be(firstUserName);
            firstUser.Id.Should().Be(firstId);
            (await users.CheckPasswordAsync(firstUser, adminFirst ? AdminPassword : UserPassword)).Should().BeTrue();
            (await users.GetRolesAsync(firstUser)).Should().Equal(adminFirst ? "admin" : "user");

            await events.Received(1).SendAsync(Arg.Is<UserCreated>(message => message.Id == firstId));
            events.ReceivedCalls().Should().HaveCount(1);
        }
    }

    private static Task SeedAsync(IServiceProvider services, IdentitySeedOptions options) =>
        ActivatorUtilities.CreateInstance<IdentityDataSeeder>(services, Options.Create(options)).SeedAllAsync();

    private sealed class NoMigrationsAssembly : IMigrationsAssembly
    {
        public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = new Dictionary<string, TypeInfo>();
        public ModelSnapshot? ModelSnapshot => null;
        public Assembly Assembly => typeof(IdentityContext).Assembly;

        public string? FindMigrationId(string nameOrId) => null;

        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) =>
            throw new NotSupportedException();
    }
}
