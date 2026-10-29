using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Constants;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityDataSeederTests
{
    private const string AdminPassword = "admin-pass-from-config";
    private const string UserPassword = "user-pass-from-config";

    [Fact]
    public async Task should_skip_users_and_warn_when_seed_passwords_are_not_configured_but_seed_roles()
    {
        await using var harness = await SeederHarness.CreateAsync(new IdentitySeedOptions());

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.Roles.Select(role => role.Name).ToListAsync())
            .Should()
            .BeEquivalentTo(IdentityConstant.Role.Admin, IdentityConstant.Role.User);
        harness.Events.Should().BeEmpty();

        var warnings = harness.Logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Should().HaveCount(2);
        warnings.Should().ContainSingle(entry => entry.Message.Contains("IdentitySeedOptions:AdminPassword"));
        warnings.Should().ContainSingle(entry => entry.Message.Contains("IdentitySeedOptions:UserPassword"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_treat_whitespace_seed_passwords_as_unset(string password)
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = password, UserPassword = password }
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
        harness.Logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Should().HaveCount(2);
    }

    [Fact]
    public async Task should_seed_only_admin_when_only_admin_password_is_configured()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        await harness.Seeder.SeedAllAsync();

        var admin = await harness.UserManager.FindByNameAsync("samh");
        admin.Should().NotBeNull();
        (await harness.UserManager.CheckPasswordAsync(admin!, AdminPassword)).Should().BeTrue();
        (await harness.UserManager.GetRolesAsync(admin!)).Should().BeEquivalentTo(IdentityConstant.Role.Admin);
        (await harness.UserManager.FindByNameAsync("samh2")).Should().BeNull();

        var userCreated = harness.Events.Should().ContainSingle().Which.Should().BeOfType<UserCreated>().Subject;
        userCreated.Id.Should().Be(admin!.Id);
        userCreated.Name.Should().Be("Sam H");
        userCreated.PassportNumber.Should().Be("12345678");
        harness
            .Logger.Entries.Should()
            .ContainSingle(entry => entry.Level == LogLevel.Warning)
            .Which.Message.Should()
            .Contain("IdentitySeedOptions:UserPassword");
    }

    [Fact]
    public async Task should_seed_only_user_when_only_user_password_is_configured()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { UserPassword = UserPassword }
        );

        await harness.Seeder.SeedAllAsync();

        var user = await harness.UserManager.FindByNameAsync("samh2");
        user.Should().NotBeNull();
        (await harness.UserManager.CheckPasswordAsync(user!, UserPassword)).Should().BeTrue();
        (await harness.UserManager.GetRolesAsync(user!)).Should().BeEquivalentTo(IdentityConstant.Role.User);
        (await harness.UserManager.FindByNameAsync("samh")).Should().BeNull();

        var userCreated = harness.Events.Should().ContainSingle().Which.Should().BeOfType<UserCreated>().Subject;
        userCreated.Name.Should().Be("Sam2 H2");
        harness
            .Logger.Entries.Should()
            .ContainSingle(entry => entry.Level == LogLevel.Warning)
            .Which.Message.Should()
            .Contain("IdentitySeedOptions:AdminPassword");
    }

    [Fact]
    public async Task should_seed_both_users_with_configured_passwords_and_roles()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
        );

        await harness.Seeder.SeedAllAsync();

        var admin = await harness.UserManager.FindByNameAsync("samh");
        var user = await harness.UserManager.FindByNameAsync("samh2");

        admin.Should().NotBeNull();
        user.Should().NotBeNull();
        (await harness.UserManager.CheckPasswordAsync(admin!, AdminPassword)).Should().BeTrue();
        (await harness.UserManager.CheckPasswordAsync(user!, UserPassword)).Should().BeTrue();
        (await harness.UserManager.CheckPasswordAsync(admin!, "Admin@123456")).Should().BeFalse();
        (await harness.UserManager.CheckPasswordAsync(user!, "User@123456")).Should().BeFalse();
        (await harness.UserManager.GetRolesAsync(admin!)).Should().BeEquivalentTo(IdentityConstant.Role.Admin);
        (await harness.UserManager.GetRolesAsync(user!)).Should().BeEquivalentTo(IdentityConstant.Role.User);
        harness.Events.OfType<UserCreated>().Should().HaveCount(2);
        harness.Logger.Entries.Should().NotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_continue_seeding_user_when_admin_password_violates_policy()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = "abc", UserPassword = UserPassword }
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.UserManager.FindByNameAsync("samh")).Should().BeNull();
        (await harness.UserManager.FindByNameAsync("samh2")).Should().NotBeNull();
        harness.Events.OfType<UserCreated>().Should().ContainSingle().Which.Name.Should().Be("Sam2 H2");
    }

    [Fact]
    public async Task should_not_seed_anything_when_migrations_are_pending()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword },
            pendingMigrations: true
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        (await harness.Context.Roles.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();
    }

    private sealed class SeederHarness : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private SeederHarness(SqliteConnection connection, ServiceProvider provider)
        {
            _connection = connection;
            _provider = provider;
            _scope = provider.CreateAsyncScope();

            Context = _scope.ServiceProvider.GetRequiredService<IdentityContext>();
            UserManager = _scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            Seeder = _scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
            Events = _scope.ServiceProvider.GetRequiredService<RecordingEventDispatcher>().Events;
            Logger = _scope.ServiceProvider.GetRequiredService<RecordingLogger<IdentityDataSeeder>>();
        }

        public IdentityContext Context { get; }
        public UserManager<User> UserManager { get; }
        public IdentityDataSeeder Seeder { get; }
        public List<object> Events { get; }
        public RecordingLogger<IdentityDataSeeder> Logger { get; }

        public static async Task<SeederHarness> CreateAsync(
            IdentitySeedOptions seedOptions,
            bool pendingMigrations = false
        )
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<RecordingLogger<IdentityDataSeeder>>();
            services.AddSingleton<ILogger<IdentityDataSeeder>>(provider =>
                provider.GetRequiredService<RecordingLogger<IdentityDataSeeder>>()
            );
            services.AddSingleton<RecordingEventDispatcher>();
            services.AddSingleton<IEventDispatcher>(provider =>
                provider.GetRequiredService<RecordingEventDispatcher>()
            );
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["IdentitySeedOptions:AdminPassword"] = seedOptions.AdminPassword,
                            ["IdentitySeedOptions:UserPassword"] = seedOptions.UserPassword,
                        }
                    )
                    .Build()
            );
            services.AddScoped<IdentityDataSeeder>();

            services.AddDbContext<IdentityContext>(options =>
            {
                options.UseSqlite(connection);
                if (!pendingMigrations)
                {
                    options.ReplaceService<IMigrationsAssembly, NoMigrationsAssembly>();
                }
            });

            services
                .AddIdentityCore<User>(options =>
                {
                    options.Password.RequiredLength = 6;
                    options.Password.RequireDigit = false;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequireUppercase = false;
                })
                .AddRoles<Role>()
                .AddEntityFrameworkStores<IdentityContext>();

            var provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.EnsureCreatedAsync();
            }

            return new SeederHarness(connection, provider);
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
        public Assembly Assembly => typeof(NoMigrationsAssembly).Assembly;

        public string? FindMigrationId(string nameOrId) => null;

        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingEventDispatcher : IEventDispatcher
    {
        public List<object> Events { get; } = new();

        public Task SendAsync<T>(
            IReadOnlyList<T> events,
            Type? type = null,
            CancellationToken cancellationToken = default
        )
            where T : IEvent
        {
            Events.AddRange(events.Cast<object>());
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T @event, Type? type = null, CancellationToken cancellationToken = default)
            where T : IEvent
        {
            Events.Add(@event!);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
