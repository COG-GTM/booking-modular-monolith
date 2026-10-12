using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Data.Seed;

public class IdentityDataSeederTests : IAsyncLifetime
{
    private const string AdminPassword = "Adm1n-s33d-password";
    private const string UserPassword = "Us3r-s33d-password";

    private readonly SqliteConnection _connection;
    private readonly IdentityContext _identityContext;
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<Role> _roleManager;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly IWebHostEnvironment _environment;
    private readonly FakeLogger<IdentityDataSeeder> _logger;

    public IdentityDataSeederTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<IdentityContext>()
            .UseSqlite(_connection)
            .ReplaceService<IMigrationsAssembly, NoMigrationsAssembly>()
            .Options;
        _identityContext = new IdentityContext(options);

        _userManager = Substitute.For<UserManager<User>>(
            Substitute.For<IUserStore<User>>(),
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
        _userManager.FindByNameAsync(Arg.Any<string>()).Returns((User)null);
        _userManager.CreateAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);

        _roleManager = Substitute.For<RoleManager<Role>>(Substitute.For<IRoleStore<Role>>(), null, null, null, null);
        _roleManager.RoleExistsAsync(Arg.Any<string>()).Returns(false);
        _roleManager.CreateAsync(Arg.Any<Role>()).Returns(IdentityResult.Success);

        _eventDispatcher = Substitute.For<IEventDispatcher>();

        _environment = Substitute.For<IWebHostEnvironment>();
        _environment.EnvironmentName.Returns(Environments.Development);

        _logger = new FakeLogger<IdentityDataSeeder>();
    }

    public async Task InitializeAsync()
    {
        await _identityContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _identityContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IdentityDataSeeder CreateSeeder(AuthOptions authOptions) =>
        new(_userManager, _roleManager, _eventDispatcher, _identityContext, _environment, authOptions, _logger);

    private static AuthOptions SeedingEnabled() =>
        new()
        {
            SeedDefaultUsers = true,
            SeedAdminPassword = AdminPassword,
            SeedUserPassword = UserPassword,
        };

    [Fact]
    public async Task should_seed_default_users_with_the_configured_passwords_in_development()
    {
        var seeder = CreateSeeder(SeedingEnabled());

        await seeder.SeedAllAsync();

        var admin = InitialData.Users.First();
        var user = InitialData.Users.Last();

        await _userManager.Received(1).CreateAsync(admin, AdminPassword);
        await _userManager.Received(1).AddToRoleAsync(admin, IdentityConstant.Role.Admin);
        await _userManager.Received(1).CreateAsync(user, UserPassword);
        await _userManager.Received(1).AddToRoleAsync(user, IdentityConstant.Role.User);

        await _eventDispatcher
            .Received(2)
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        await _eventDispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<UserCreated>(e =>
                    e.Id == admin.Id
                    && e.Name == $"{admin.FirstName} {admin.LastName}"
                    && e.PassportNumber == admin.PassPortNumber
                ),
                Arg.Any<Type>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task should_not_use_the_legacy_hardcoded_passwords()
    {
        var seeder = CreateSeeder(SeedingEnabled());

        await seeder.SeedAllAsync();

        await _userManager.DidNotReceive().CreateAsync(Arg.Any<User>(), "Admin@123456");
        await _userManager.DidNotReceive().CreateAsync(Arg.Any<User>(), "User@123456");
    }

    [Fact]
    public async Task should_not_seed_users_when_seeding_is_disabled()
    {
        var authOptions = SeedingEnabled();
        authOptions.SeedDefaultUsers = false;
        var seeder = CreateSeeder(authOptions);

        await seeder.SeedAllAsync();

        await _userManager.DidNotReceiveWithAnyArgs().CreateAsync(default, default);
        await _eventDispatcher
            .DidNotReceive()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        _logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_still_seed_roles_when_user_seeding_is_disabled()
    {
        var authOptions = SeedingEnabled();
        authOptions.SeedDefaultUsers = false;
        var seeder = CreateSeeder(authOptions);

        await seeder.SeedAllAsync();

        await _roleManager.Received(1).CreateAsync(Arg.Is<Role>(r => r.Name == IdentityConstant.Role.Admin));
        await _roleManager.Received(1).CreateAsync(Arg.Is<Role>(r => r.Name == IdentityConstant.Role.User));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("test")]
    public async Task should_not_seed_users_outside_development_and_log_a_warning(string environmentName)
    {
        _environment.EnvironmentName.Returns(environmentName);
        var seeder = CreateSeeder(SeedingEnabled());

        await seeder.SeedAllAsync();

        await _userManager.DidNotReceiveWithAnyArgs().CreateAsync(default, default);
        _logger
            .Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should()
            .Contain(environmentName)
            .And.Contain($"{nameof(AuthOptions)}:{nameof(AuthOptions.SeedDefaultUsers)}");
    }

    [Theory]
    [InlineData(null, UserPassword)]
    [InlineData("", UserPassword)]
    [InlineData("   ", UserPassword)]
    [InlineData(AdminPassword, null)]
    [InlineData(AdminPassword, "")]
    [InlineData(null, null)]
    public async Task should_not_seed_users_when_a_seed_password_is_missing_and_log_a_warning(
        string adminPassword,
        string userPassword
    )
    {
        var authOptions = new AuthOptions
        {
            SeedDefaultUsers = true,
            SeedAdminPassword = adminPassword,
            SeedUserPassword = userPassword,
        };
        var seeder = CreateSeeder(authOptions);

        await seeder.SeedAllAsync();

        await _userManager.DidNotReceiveWithAnyArgs().CreateAsync(default, default);
        await _eventDispatcher
            .DidNotReceive()
            .SendAsync(Arg.Any<UserCreated>(), Arg.Any<Type>(), Arg.Any<CancellationToken>());
        _logger
            .Entries.Should()
            .ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should()
            .Contain($"{nameof(AuthOptions)}:{nameof(AuthOptions.SeedAdminPassword)}")
            .And.Contain($"{nameof(AuthOptions)}:{nameof(AuthOptions.SeedUserPassword)}");
    }

    [Fact]
    public async Task should_not_seed_users_when_users_already_exist()
    {
        _identityContext.Users.Add(
            new User
            {
                Id = Guid.NewGuid(),
                UserName = "existing",
                FirstName = "Existing",
                LastName = "User",
                PassPortNumber = "00000000",
                SecurityStamp = Guid.NewGuid().ToString(),
            }
        );
        await _identityContext.SaveChangesAsync();
        var seeder = CreateSeeder(SeedingEnabled());

        await seeder.SeedAllAsync();

        await _userManager.DidNotReceiveWithAnyArgs().CreateAsync(default, default);
    }

    [Fact]
    public async Task should_not_create_a_default_user_that_already_exists_by_name()
    {
        var admin = InitialData.Users.First();
        _userManager.FindByNameAsync(admin.UserName).Returns(admin);
        var seeder = CreateSeeder(SeedingEnabled());

        await seeder.SeedAllAsync();

        await _userManager.DidNotReceive().CreateAsync(admin, Arg.Any<string>());
        await _userManager.Received(1).CreateAsync(InitialData.Users.Last(), UserPassword);
    }
}
