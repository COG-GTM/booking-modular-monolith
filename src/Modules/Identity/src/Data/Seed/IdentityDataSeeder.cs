using System;
using System.Linq;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using Identity.Configurations;
using Identity.Identity.Constants;
using Identity.Identity.Models;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Identity.Data.Seed;

public class IdentityDataSeeder : IDataSeeder
{
    private readonly UserManager<User> _userManager;
    private readonly RoleManager<Role> _roleManager;
    private readonly IEventDispatcher _eventDispatcher;
    private readonly IdentityContext _identityContext;
    private readonly IdentitySeedOptions _seedOptions;
    private readonly ILogger<IdentityDataSeeder> _logger;

    public IdentityDataSeeder(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        IEventDispatcher eventDispatcher,
        IdentityContext identityContext,
        IdentitySeedOptions seedOptions,
        ILogger<IdentityDataSeeder> logger
    )
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _eventDispatcher = eventDispatcher;
        _identityContext = identityContext;
        _seedOptions = seedOptions;
        _logger = logger;
    }

    public async Task SeedAllAsync()
    {
        var pendingMigrations = await _identityContext.Database.GetPendingMigrationsAsync();

        if (!pendingMigrations.Any())
        {
            await SeedRoles();
            await SeedInitialAdminUser();
        }
    }

    private async Task SeedRoles()
    {
        if (!await _identityContext.Roles.AnyAsync())
        {
            if (await _roleManager.RoleExistsAsync(IdentityConstant.Role.Admin) == false)
            {
                await _roleManager.CreateAsync(new Role { Name = IdentityConstant.Role.Admin });
            }

            if (await _roleManager.RoleExistsAsync(IdentityConstant.Role.User) == false)
            {
                await _roleManager.CreateAsync(new Role { Name = IdentityConstant.Role.User });
            }
        }
    }

    // No accounts are created unless an operator explicitly opts in and supplies the
    // initial admin password through configuration (environment variable / user secrets /
    // secret store). The repository never ships a default password.
    private async Task SeedInitialAdminUser()
    {
        if (!_seedOptions.Enabled)
        {
            _logger.LogInformation("Identity user seeding is disabled; no initial admin account will be created.");
            return;
        }

        if (await _identityContext.Users.AnyAsync())
        {
            return;
        }

        if (
            string.IsNullOrWhiteSpace(_seedOptions.AdminUserName)
            || string.IsNullOrWhiteSpace(_seedOptions.AdminEmail)
            || string.IsNullOrWhiteSpace(_seedOptions.AdminPassword)
        )
        {
            _logger.LogWarning(
                "Identity user seeding is enabled but {Section}:AdminUserName, AdminEmail or AdminPassword is not configured; skipping initial admin creation.",
                nameof(IdentitySeedOptions)
            );
            return;
        }

        if (await _userManager.FindByNameAsync(_seedOptions.AdminUserName) != null)
        {
            return;
        }

        var admin = new User
        {
            Id = NewId.NextGuid(),
            UserName = _seedOptions.AdminUserName,
            Email = _seedOptions.AdminEmail,
            FirstName = _seedOptions.AdminFirstName ?? string.Empty,
            LastName = _seedOptions.AdminLastName ?? string.Empty,
            PassPortNumber = _seedOptions.AdminPassPortNumber ?? string.Empty,
            SecurityStamp = Guid.NewGuid().ToString(),
        };

        var result = await _userManager.CreateAsync(admin, _seedOptions.AdminPassword);

        if (!result.Succeeded)
        {
            _logger.LogError(
                "Failed to create initial admin user {UserName}: {Errors}",
                admin.UserName,
                string.Join("; ", result.Errors.Select(e => e.Description))
            );
            return;
        }

        await _userManager.AddToRoleAsync(admin, IdentityConstant.Role.Admin);

        await _eventDispatcher.SendAsync(
            new UserCreated(admin.Id, admin.FirstName + " " + admin.LastName, admin.PassPortNumber)
        );

        _logger.LogInformation("Initial admin user {UserName} created from configuration.", admin.UserName);
    }
}
