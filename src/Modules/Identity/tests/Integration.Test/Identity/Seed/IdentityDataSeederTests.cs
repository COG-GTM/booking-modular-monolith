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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Integration.Test.Identity.Seed;

[CollectionDefinition("Identity seed data", DisableParallelization = true)]
public class IdentitySeedCollection;

[Collection("Identity seed data")]
public class IdentityDataSeederTests
{
    private const string AdminPassword = "Configured-Admin-Pw1";
    private const string UserPassword = "Configured-User-Pw1";

    [Theory]
    [InlineData(AdminPassword, null, "samh", "admin", AdminPassword, "samh2")]
    [InlineData(null, UserPassword, "samh2", "user", UserPassword, "samh")]
    [InlineData(AdminPassword, " \t ", "samh", "admin", AdminPassword, "samh2")]
    [InlineData(" \t ", UserPassword, "samh2", "user", UserPassword, "samh")]
    public async Task should_seed_only_user_with_configured_password(
        string? adminPassword,
        string? userPassword,
        string name,
        string role,
        string password,
        string skippedName
    )
    {
        await using var harness = await Harness.Create(adminPassword, userPassword);

        await harness.Seeder.SeedAllAsync();

        var user = await harness.Users.FindByNameAsync(name);
        user.Should().NotBeNull();
        (await harness.Users.CheckPasswordAsync(user!, password)).Should().BeTrue();
        (await harness.Users.GetRolesAsync(user!)).Should().Equal(role);
        (await harness.Users.FindByNameAsync(skippedName)).Should().BeNull();
        (await harness.Context.Users.CountAsync()).Should().Be(1);
        await harness
            .Events.Received(1)
            .SendAsync(
                Arg.Is<UserCreated>(e =>
                    e.Id == user!.Id
                    && e.Name == user.FirstName + " " + user.LastName
                    && e.PassportNumber == user.PassPortNumber
                )
            );
        harness.Events.ReceivedCalls().Should().HaveCount(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public async Task should_not_create_users_or_dispatch_events_for_missing_passwords(string? password)
    {
        await using var harness = await Harness.Create(password, password);

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.UserRoles.CountAsync()).Should().Be(0);
        harness.Events.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_dispatch_once_for_each_created_user_and_not_on_reseed()
    {
        await using var harness = await Harness.Create(AdminPassword, UserPassword);

        await harness.Seeder.SeedAllAsync();
        await harness.Seeder.SeedAllAsync();

        var users = await harness.Context.Users.ToListAsync();
        users.Should().HaveCount(2);
        foreach (var user in users)
        {
            await harness.Events.Received(1).SendAsync(Arg.Is<UserCreated>(e => e.Id == user.Id));
        }
        harness.Events.ReceivedCalls().Should().HaveCount(2);
    }

    [Theory]
    [InlineData("samh", "admin")]
    [InlineData("samh2", "user")]
    public async Task should_skip_existing_user_by_name(string name, string role)
    {
        await using var harness = await Harness.Create(AdminPassword, UserPassword);
        var existing = new User
        {
            UserName = name,
            FirstName = "Existing",
            LastName = "User",
            PassPortNumber = "existing",
        };
        (await harness.Users.CreateAsync(existing, AdminPassword)).Succeeded.Should().BeTrue();
        var candidate = new User
        {
            UserName = name,
            FirstName = "New",
            LastName = "User",
            PassPortNumber = "new",
        };

        var seedUser = typeof(IdentityDataSeeder).GetMethod(
            "SeedUser",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await (Task)seedUser.Invoke(harness.Seeder, new object[] { candidate, UserPassword, role, "UserPassword" })!;

        (await harness.Context.Users.SingleAsync()).Id.Should().Be(existing.Id);
        (await harness.Users.CheckPasswordAsync(existing, AdminPassword)).Should().BeTrue();
        (await harness.Context.UserRoles.CountAsync()).Should().Be(0);
        harness.Events.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task should_not_throw_add_roles_or_dispatch_when_password_policy_rejects_creation()
    {
        await using var harness = await Harness.Create("weak", "weak");

        var seed = () => harness.Seeder.SeedAllAsync();
        await seed.Should().NotThrowAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.UserRoles.CountAsync()).Should().Be(0);
        harness.Events.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public void should_bind_passwords_from_identity_seed_options_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["IdentitySeedOptions:AdminPassword"] = AdminPassword,
                    ["IdentitySeedOptions:UserPassword"] = UserPassword,
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.Configure<IdentitySeedOptions>(configuration.GetSection(nameof(IdentitySeedOptions)));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;

        options.AdminPassword.Should().Be(AdminPassword);
        options.UserPassword.Should().Be(UserPassword);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        public IdentityContext Context => _scope.ServiceProvider.GetRequiredService<IdentityContext>();
        public UserManager<User> Users => _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        public IEventDispatcher Events { get; } = Substitute.For<IEventDispatcher>();
        public IdentityDataSeeder Seeder { get; }

        private Harness(SqliteConnection connection, string? adminPassword, string? userPassword)
        {
            _connection = connection;
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<IdentityContext>(options =>
                options.UseSqlite(connection).ReplaceService<IMigrationsAssembly, NoMigrationsAssembly>()
            );
            services.AddIdentity<User, Role>().AddEntityFrameworkStores<IdentityContext>();
            services.Configure<IdentitySeedOptions>(options =>
            {
                options.AdminPassword = adminPassword;
                options.UserPassword = userPassword;
            });
            services.AddSingleton(Events);
            _provider = services.BuildServiceProvider();
            _scope = _provider.CreateAsyncScope();
            Seeder = ActivatorUtilities.CreateInstance<IdentityDataSeeder>(_scope.ServiceProvider);
        }

        public static async Task<Harness> Create(string? adminPassword, string? userPassword)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var harness = new Harness(connection, adminPassword, userPassword);
            await harness.Context.Database.EnsureCreatedAsync();
            return harness;
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

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
