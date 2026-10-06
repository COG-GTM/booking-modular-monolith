using BuildingBlocks.Constants;
using BuildingBlocks.Core;
using BuildingBlocks.Web;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Unit.Test.Data.Seed;

public class IdentityDataSeederTests
{
    private const string ConfiguredAdminPassword = "configured-admin-pass";
    private const string ConfiguredUserPassword = "configured-user-pass";

    [Fact]
    public async Task should_not_seed_any_user_when_no_admin_password_is_configured()
    {
        await using var fixture = await SeederFixture.CreateAsync(Environments.Production, new IdentitySeedOptions());

        await fixture.Seeder.SeedAllAsync();

        (await fixture.Context.Users.AnyAsync()).Should().BeFalse();
        (await fixture.Context.Roles.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task should_seed_admin_with_configured_password_and_skip_demo_user_outside_development()
    {
        await using var fixture = await SeederFixture.CreateAsync(
            Environments.Production,
            new IdentitySeedOptions { AdminPassword = ConfiguredAdminPassword, UserPassword = ConfiguredUserPassword }
        );

        await fixture.Seeder.SeedAllAsync();

        var admin = await fixture.UserManager.FindByNameAsync(InitialData.Users.First().UserName!);
        admin.Should().NotBeNull();
        (await fixture.UserManager.CheckPasswordAsync(admin!, ConfiguredAdminPassword)).Should().BeTrue();
        (await fixture.UserManager.CheckPasswordAsync(admin!, "Admin@123456")).Should().BeFalse();
        (await fixture.UserManager.IsInRoleAsync(admin!, IdentityConstant.Role.Admin)).Should().BeTrue();

        (await fixture.UserManager.FindByNameAsync(InitialData.Users.Last().UserName!)).Should().BeNull();
        (await fixture.Context.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task should_seed_demo_user_with_configured_password_in_development()
    {
        await using var fixture = await SeederFixture.CreateAsync(
            Environments.Development,
            new IdentitySeedOptions { AdminPassword = ConfiguredAdminPassword, UserPassword = ConfiguredUserPassword }
        );

        await fixture.Seeder.SeedAllAsync();

        var user = await fixture.UserManager.FindByNameAsync(InitialData.Users.Last().UserName!);
        user.Should().NotBeNull();
        (await fixture.UserManager.CheckPasswordAsync(user!, ConfiguredUserPassword)).Should().BeTrue();
        (await fixture.UserManager.IsInRoleAsync(user!, IdentityConstant.Role.User)).Should().BeTrue();
        (await fixture.Context.Users.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task should_seed_admin_on_later_start_when_demo_user_already_exists()
    {
        var options = new IdentitySeedOptions { UserPassword = ConfiguredUserPassword };
        await using var fixture = await SeederFixture.CreateAsync(Environments.Development, options);

        await fixture.Seeder.SeedAllAsync();
        (await fixture.Context.Users.CountAsync()).Should().Be(1);

        options.AdminPassword = ConfiguredAdminPassword;
        await fixture.Seeder.SeedAllAsync();

        var admin = await fixture.UserManager.FindByNameAsync(InitialData.Users.First().UserName!);
        admin.Should().NotBeNull();
        (await fixture.UserManager.IsInRoleAsync(admin!, IdentityConstant.Role.Admin)).Should().BeTrue();
        (await fixture.Context.Users.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task should_not_seed_admin_when_configured_password_violates_policy()
    {
        await using var fixture = await SeederFixture.CreateAsync(
            Environments.Production,
            new IdentitySeedOptions { AdminPassword = "short" }
        );

        await fixture.Seeder.SeedAllAsync();

        (await fixture.Context.Users.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public void should_bind_seed_options_from_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["IdentitySeedOptions:AdminPassword"] = ConfiguredAdminPassword,
                    ["IdentitySeedOptions:UserPassword"] = ConfiguredUserPassword,
                }
            )
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddValidateOptions<IdentitySeedOptions>();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IdentitySeedOptions>();

        options.AdminPassword.Should().Be(ConfiguredAdminPassword);
        options.UserPassword.Should().Be(ConfiguredUserPassword);
    }

    private sealed class SeederFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private SeederFixture(SqliteConnection connection, ServiceProvider provider)
        {
            _connection = connection;
            _provider = provider;
            _scope = provider.CreateAsyncScope();
        }

        public IdentityDataSeeder Seeder => _scope.ServiceProvider.GetRequiredService<IdentityDataSeeder>();
        public IdentityContext Context => _scope.ServiceProvider.GetRequiredService<IdentityContext>();
        public UserManager<User> UserManager => _scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        public static async Task<SeederFixture> CreateAsync(string environmentName, IdentitySeedOptions seedOptions)
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();

            var env = Substitute.For<IWebHostEnvironment>();
            env.EnvironmentName.Returns(environmentName);

            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<IdentityContext>(options =>
                options
                    .UseSqlite(
                        connection,
                        sqlite => sqlite.MigrationsAssembly(typeof(IdentityDataSeederTests).Assembly.GetName().Name)
                    )
                    .UseSnakeCaseNamingConvention()
            );

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

            services.AddSingleton(env);
            services.AddSingleton(seedOptions);
            services.AddSingleton(Substitute.For<IEventDispatcher>());
            services.AddScoped<IdentityDataSeeder>();

            var provider = services.BuildServiceProvider();

            await using (var scope = provider.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IdentityContext>().Database.EnsureCreatedAsync();
            }

            return new SeederFixture(connection, provider);
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }
}
