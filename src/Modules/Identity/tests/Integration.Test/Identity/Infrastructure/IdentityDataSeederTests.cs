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
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityDataSeederTests
{
    private const string AdminPassword = "admin-pass-from-config";
    private const string UserPassword = "user-pass-from-config";

    [Fact]
    public async Task should_skip_user_seeding_and_warn_when_no_seed_passwords_are_configured()
    {
        await using var harness = await SeederHarness.CreateAsync(new IdentitySeedOptions());

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Users.CountAsync()).Should().Be(0);
        harness.Events.Should().BeEmpty();

        var warnings = harness.Logger.Entries.Where(e => e.Level == LogLevel.Warning).Select(e => e.Message).ToList();
        warnings.Should().HaveCount(2);
        warnings.Should().ContainSingle(m => m.Contains("'samh'") && m.Contains("IdentitySeedOptions:AdminPassword"));
        warnings.Should().ContainSingle(m => m.Contains("'samh2'") && m.Contains("IdentitySeedOptions:UserPassword"));
    }

    [Fact]
    public async Task should_still_seed_roles_when_no_seed_passwords_are_configured()
    {
        await using var harness = await SeederHarness.CreateAsync(new IdentitySeedOptions());

        await harness.Seeder.SeedAllAsync();

        (await harness.Context.Roles.Select(r => r.Name).ToListAsync())
            .Should()
            .BeEquivalentTo(IdentityConstant.Role.Admin, IdentityConstant.Role.User);
    }

    [Fact]
    public async Task should_seed_only_admin_user_when_only_admin_password_is_configured()
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
            .ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should()
            .Contain("IdentitySeedOptions:UserPassword");
    }

    [Fact]
    public async Task should_seed_missing_user_on_a_later_run_when_its_password_becomes_configured()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        await harness.Seeder.SeedAllAsync();
        (await harness.UserManager.FindByNameAsync("samh2")).Should().BeNull();

        var secondRun = harness.CreateSeeder(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
        );

        await secondRun.SeedAllAsync();

        var user = await harness.UserManager.FindByNameAsync("samh2");
        user.Should().NotBeNull();
        (await harness.UserManager.CheckPasswordAsync(user!, UserPassword)).Should().BeTrue();
        (await harness.UserManager.GetRolesAsync(user!)).Should().BeEquivalentTo(IdentityConstant.Role.User);
        (await harness.Context.Users.CountAsync(u => u.UserName == "samh")).Should().Be(1);
        harness.Events.OfType<UserCreated>().Should().HaveCount(2);
    }

    [Fact]
    public async Task should_create_missing_role_and_seed_admin_when_roles_are_partially_present()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        (await harness.RoleManager.CreateAsync(new Role { Name = IdentityConstant.Role.User }))
            .Succeeded.Should()
            .BeTrue();
        (
            await harness.UserManager.CreateAsync(
                new User
                {
                    UserName = "existing",
                    FirstName = "Existing",
                    LastName = "User",
                    PassPortNumber = "0",
                },
                UserPassword
            )
        ).Succeeded.Should().BeTrue();

        await harness.Seeder.SeedAllAsync();

        (await harness.RoleManager.RoleExistsAsync(IdentityConstant.Role.Admin)).Should().BeTrue();
        var admin = await harness.UserManager.FindByNameAsync("samh");
        admin.Should().NotBeNull();
        (await harness.UserManager.GetRolesAsync(admin!)).Should().BeEquivalentTo(IdentityConstant.Role.Admin);
        harness.Logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task should_not_promote_pre_registered_user_sharing_the_seeded_username()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );
        var registered = new User
        {
            UserName = "samh",
            FirstName = "Some",
            LastName = "Body",
            PassPortNumber = "0",
        };
        (await harness.UserManager.CreateAsync(registered, "Attacker-Chosen-Pw1")).Succeeded.Should().BeTrue();

        await harness.Seeder.SeedAllAsync();

        var user = await harness.UserManager.FindByNameAsync("samh");
        user!.Id.Should().Be(registered.Id);
        (await harness.UserManager.GetRolesAsync(user)).Should().BeEmpty();
        (await harness.Context.Users.CountAsync()).Should().Be(1);
        harness.Events.Should().BeEmpty();
        harness.Logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
        harness
            .Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning && e.Message.Contains("'samh'"))
            .Which.Message.Should()
            .Contain("unrelated account already uses that username");
    }

    [Fact]
    public async Task should_seed_both_users_with_their_roles_when_both_passwords_are_configured()
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
        (await harness.UserManager.GetRolesAsync(admin!)).Should().BeEquivalentTo(IdentityConstant.Role.Admin);
        (await harness.UserManager.GetRolesAsync(user!)).Should().BeEquivalentTo(IdentityConstant.Role.User);

        harness.Events.OfType<UserCreated>().Select(e => e.Id).Should().BeEquivalentTo(new[] { admin!.Id, user!.Id });
        harness.Logger.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task should_not_seed_users_with_the_previously_hardcoded_passwords()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword, UserPassword = UserPassword }
        );

        await harness.Seeder.SeedAllAsync();

        var admin = await harness.UserManager.FindByNameAsync("samh");
        var user = await harness.UserManager.FindByNameAsync("samh2");

        (await harness.UserManager.CheckPasswordAsync(admin!, "Admin@123456")).Should().BeFalse();
        (await harness.UserManager.CheckPasswordAsync(user!, "User@123456")).Should().BeFalse();
    }

    [Fact]
    public async Task should_log_error_and_not_dispatch_event_when_seed_password_violates_policy()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = "short", UserPassword = UserPassword }
        );

        await harness.Seeder.SeedAllAsync();

        (await harness.UserManager.FindByNameAsync("samh")).Should().BeNull();
        (await harness.UserManager.FindByNameAsync("samh2")).Should().NotBeNull();

        harness
            .Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Error)
            .Which.Message.Should()
            .Contain("'samh'")
            .And.Contain("failed");
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

    [Fact]
    public async Task should_roll_back_user_and_not_dispatch_event_when_role_assignment_fails()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );
        var failingUserManager = harness.Resolve<RoleAssignmentFailingUserManager>();

        await harness
            .CreateSeeder(new IdentitySeedOptions { AdminPassword = AdminPassword }, failingUserManager)
            .SeedAllAsync();

        (await harness.UserManager.FindByNameAsync("samh")).Should().BeNull();
        harness.Events.Should().BeEmpty();
        harness
            .Logger.Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Error)
            .Which.Message.Should()
            .Contain("'admin'")
            .And.Contain("'samh'")
            .And.Contain("role store unavailable");
    }

    [Fact]
    public async Task should_seed_user_on_a_later_run_after_role_assignment_failure_was_rolled_back()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        await harness
            .CreateSeeder(
                new IdentitySeedOptions { AdminPassword = AdminPassword },
                harness.Resolve<RoleAssignmentFailingUserManager>()
            )
            .SeedAllAsync();
        (await harness.UserManager.FindByNameAsync("samh")).Should().BeNull();

        await harness.Seeder.SeedAllAsync();

        var admin = await harness.UserManager.FindByNameAsync("samh");
        admin.Should().NotBeNull();
        (await harness.UserManager.GetRolesAsync(admin!)).Should().BeEquivalentTo(IdentityConstant.Role.Admin);
        harness.Events.OfType<UserCreated>().Should().ContainSingle().Which.Id.Should().Be(admin!.Id);
    }

    [Fact]
    public async Task should_log_rollback_failure_and_keep_user_when_delete_after_failed_role_assignment_fails()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        await harness
            .CreateSeeder(
                new IdentitySeedOptions { AdminPassword = AdminPassword },
                harness.Resolve<RollbackFailingUserManager>()
            )
            .SeedAllAsync();

        var admin = await harness.UserManager.FindByNameAsync("samh");
        admin.Should().NotBeNull();
        (await harness.UserManager.GetRolesAsync(admin!)).Should().BeEmpty();
        harness.Events.Should().BeEmpty();

        var errors = harness.Logger.Entries.Where(e => e.Level == LogLevel.Error).Select(e => e.Message).ToList();
        errors.Should().HaveCount(2);
        errors
            .Should()
            .ContainSingle(m => m.Contains("Assigning role 'admin'") && m.Contains("role store unavailable"));
        errors
            .Should()
            .ContainSingle(m =>
                m.Contains("Rolling back seeded user 'samh' failed")
                && m.Contains("exists without role 'admin'")
                && m.Contains("must be fixed manually")
                && m.Contains("user store unavailable")
            );
    }

    [Fact]
    public async Task should_not_touch_stranded_user_on_a_later_run_when_rollback_of_failed_role_assignment_failed()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );

        await harness
            .CreateSeeder(
                new IdentitySeedOptions { AdminPassword = AdminPassword },
                harness.Resolve<RollbackFailingUserManager>()
            )
            .SeedAllAsync();
        var stranded = await harness.UserManager.FindByNameAsync("samh");
        stranded.Should().NotBeNull();
        harness.Logger.Entries.Clear();

        await harness.Seeder.SeedAllAsync();

        var user = await harness.UserManager.FindByNameAsync("samh");
        user!.Id.Should().Be(stranded!.Id);
        (await harness.UserManager.GetRolesAsync(user)).Should().BeEmpty();
        (await harness.Context.Users.CountAsync()).Should().Be(1);
        harness.Events.Should().BeEmpty();
        harness.Logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task should_not_attempt_role_assignment_on_existing_user_sharing_the_seeded_username()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );
        var existing = InitialData.Users.First();
        (await harness.UserManager.CreateAsync(existing, AdminPassword)).Succeeded.Should().BeTrue();

        await harness
            .CreateSeeder(
                new IdentitySeedOptions { AdminPassword = AdminPassword },
                harness.Resolve<RoleAssignmentFailingUserManager>()
            )
            .SeedAllAsync();

        var user = await harness.UserManager.FindByNameAsync("samh");
        user!.Id.Should().Be(existing.Id);
        (await harness.UserManager.GetRolesAsync(user)).Should().BeEmpty();
        harness.Events.Should().BeEmpty();
        harness.Logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task should_not_reassign_role_or_log_when_existing_seeded_user_already_has_its_role()
    {
        await using var harness = await SeederHarness.CreateAsync(
            new IdentitySeedOptions { AdminPassword = AdminPassword }
        );
        await harness.Seeder.SeedAllAsync();
        harness.Events.Clear();

        await harness
            .CreateSeeder(
                new IdentitySeedOptions { AdminPassword = AdminPassword },
                harness.Resolve<RoleAssignmentFailingUserManager>()
            )
            .SeedAllAsync();

        var admin = await harness.UserManager.FindByNameAsync("samh");
        (await harness.UserManager.GetRolesAsync(admin!)).Should().BeEquivalentTo(IdentityConstant.Role.Admin);
        harness.Events.Should().BeEmpty();
        harness.Logger.Entries.Should().NotContain(e => e.Level == LogLevel.Error);
    }

    private sealed class RollbackFailingUserManager : UserManager<User>
    {
        public RollbackFailingUserManager(
            IUserStore<User> store,
            IOptions<IdentityOptions> optionsAccessor,
            IPasswordHasher<User> passwordHasher,
            IEnumerable<IUserValidator<User>> userValidators,
            IEnumerable<IPasswordValidator<User>> passwordValidators,
            ILookupNormalizer keyNormalizer,
            IdentityErrorDescriber errors,
            IServiceProvider services,
            ILogger<UserManager<User>> logger
        )
            : base(
                store,
                optionsAccessor,
                passwordHasher,
                userValidators,
                passwordValidators,
                keyNormalizer,
                errors,
                services,
                logger
            ) { }

        public override Task<IdentityResult> AddToRoleAsync(User user, string role) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "role store unavailable" }));

        public override Task<IdentityResult> DeleteAsync(User user) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "user store unavailable" }));
    }

    private sealed class RoleAssignmentFailingUserManager : UserManager<User>
    {
        public RoleAssignmentFailingUserManager(
            IUserStore<User> store,
            IOptions<IdentityOptions> optionsAccessor,
            IPasswordHasher<User> passwordHasher,
            IEnumerable<IUserValidator<User>> userValidators,
            IEnumerable<IPasswordValidator<User>> passwordValidators,
            ILookupNormalizer keyNormalizer,
            IdentityErrorDescriber errors,
            IServiceProvider services,
            ILogger<UserManager<User>> logger
        )
            : base(
                store,
                optionsAccessor,
                passwordHasher,
                userValidators,
                passwordValidators,
                keyNormalizer,
                errors,
                services,
                logger
            ) { }

        public override Task<IdentityResult> AddToRoleAsync(User user, string role) =>
            Task.FromResult(IdentityResult.Failed(new IdentityError { Description = "role store unavailable" }));
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
            RoleManager = _scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
            Seeder = _scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
            Events = _scope.ServiceProvider.GetRequiredService<RecordingEventDispatcher>().Events;
            Logger = _scope.ServiceProvider.GetRequiredService<RecordingLogger<IdentityDataSeeder>>();
        }

        public IdentityContext Context { get; }
        public UserManager<User> UserManager { get; }
        public RoleManager<Role> RoleManager { get; }
        public IdentityDataSeeder Seeder { get; }
        public List<object> Events { get; }
        public RecordingLogger<IdentityDataSeeder> Logger { get; }

        public IdentityDataSeeder CreateSeeder(IdentitySeedOptions seedOptions) =>
            ActivatorUtilities.CreateInstance<IdentityDataSeeder>(_scope.ServiceProvider, seedOptions);

        public IdentityDataSeeder CreateSeeder(IdentitySeedOptions seedOptions, UserManager<User> userManager) =>
            ActivatorUtilities.CreateInstance<IdentityDataSeeder>(_scope.ServiceProvider, seedOptions, userManager);

        public T Resolve<T>()
            where T : notnull => ActivatorUtilities.CreateInstance<T>(_scope.ServiceProvider);

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
            services.AddSingleton<ILogger<IdentityDataSeeder>>(sp =>
                sp.GetRequiredService<RecordingLogger<IdentityDataSeeder>>()
            );
            services.AddSingleton<RecordingEventDispatcher>();
            services.AddSingleton<IEventDispatcher>(sp => sp.GetRequiredService<RecordingEventDispatcher>());
            services.AddSingleton(seedOptions);
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
                .AddIdentityCore<User>(config =>
                {
                    config.Password.RequiredLength = 6;
                    config.Password.RequireDigit = false;
                    config.Password.RequireNonAlphanumeric = false;
                    config.Password.RequireUppercase = false;
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
