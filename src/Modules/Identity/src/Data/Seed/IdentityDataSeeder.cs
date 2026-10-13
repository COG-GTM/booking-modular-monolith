using System;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using Identity.Identity.Constants;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Identity.Data.Seed;

using System.Linq;

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
            await SeedUsers();
        }
    }

    private async Task SeedRoles()
    {
        await SeedRole(IdentityConstant.Role.Admin);
        await SeedRole(IdentityConstant.Role.User);
    }

    private async Task SeedRole(string role)
    {
        if (await _roleManager.RoleExistsAsync(role))
        {
            return;
        }

        var result = await _roleManager.CreateAsync(new Role { Name = role });

        if (!result.Succeeded)
        {
            _logger.LogError(
                "Seeding role '{Role}' failed: {Errors}",
                role,
                string.Join("; ", result.Errors.Select(e => e.Description))
            );
        }
    }

    private async Task SeedUsers()
    {
        await SeedUser(
            InitialData.Users.First(),
            _seedOptions.AdminPassword,
            IdentityConstant.Role.Admin,
            $"{nameof(IdentitySeedOptions)}:{nameof(IdentitySeedOptions.AdminPassword)}"
        );

        await SeedUser(
            InitialData.Users.Last(),
            _seedOptions.UserPassword,
            IdentityConstant.Role.User,
            $"{nameof(IdentitySeedOptions)}:{nameof(IdentitySeedOptions.UserPassword)}"
        );
    }

    private async Task SeedUser(User user, string? password, string role, string passwordConfigKey)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            _logger.LogWarning(
                "Skipping seed of {Role} user '{UserName}' because {ConfigKey} is not configured.",
                role,
                user.UserName,
                passwordConfigKey
            );
            return;
        }

        var existing = await _userManager.FindByNameAsync(user.UserName!);
        if (existing != null)
        {
            // Only an account carrying the seeded id was created by this seeder; a self-registered
            // account that merely shares the username must never be promoted.
            if (existing.Id == user.Id)
            {
                await EnsureRole(existing, role);
            }
            else
            {
                _logger.LogWarning(
                    "Skipping seed of {Role} user '{UserName}' because an unrelated account already uses that username.",
                    role,
                    user.UserName
                );
            }

            return;
        }

        var result = await _userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            _logger.LogError(
                "Seeding {Role} user '{UserName}' failed: {Errors}",
                role,
                user.UserName,
                string.Join("; ", result.Errors.Select(e => e.Description))
            );
            return;
        }

        var roleResult = await _userManager.AddToRoleAsync(user, role);

        if (!roleResult.Succeeded)
        {
            _logger.LogError(
                "Assigning role '{Role}' to seeded user '{UserName}' failed, rolling back the user so the next run can retry: {Errors}",
                role,
                user.UserName,
                string.Join("; ", roleResult.Errors.Select(e => e.Description))
            );
            var deleteResult = await _userManager.DeleteAsync(user);
            if (!deleteResult.Succeeded)
            {
                _logger.LogError(
                    "Rolling back seeded user '{UserName}' failed; role '{Role}' will be reconciled on the next run: {Errors}",
                    user.UserName,
                    role,
                    string.Join("; ", deleteResult.Errors.Select(e => e.Description))
                );
            }
            return;
        }

        await _eventDispatcher.SendAsync(
            new UserCreated(user.Id, user.FirstName + " " + user.LastName, user.PassPortNumber)
        );
    }

    private async Task EnsureRole(User user, string role)
    {
        if (await _userManager.IsInRoleAsync(user, role))
        {
            return;
        }

        var result = await _userManager.AddToRoleAsync(user, role);
        if (!result.Succeeded)
        {
            _logger.LogError(
                "Assigning missing role '{Role}' to existing seeded user '{UserName}' failed: {Errors}",
                role,
                user.UserName,
                string.Join("; ", result.Errors.Select(e => e.Description))
            );
        }
    }
}
