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
            // Existing accounts are never modified by the seeder (no role or password changes),
            // so a revoked role stays revoked and an unrelated account sharing the username can
            // never be promoted.
            if (existing.Id != user.Id)
            {
                _logger.LogWarning(
                    "Skipping seed of {Role} user '{UserName}' because an unrelated account already uses that username.",
                    role,
                    user.UserName
                );
            }

            return;
        }

        // User creation and role assignment are committed together so a failure leaves no
        // half-seeded account behind and the next run retries the whole seed.
        await using var transaction = await _identityContext.Database.BeginTransactionAsync();

        var result = await _userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            await transaction.RollbackAsync();
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
            await transaction.RollbackAsync();
            _logger.LogError(
                "Assigning role '{Role}' to seeded user '{UserName}' failed; the user was not created and will be retried on the next run: {Errors}",
                role,
                user.UserName,
                string.Join("; ", roleResult.Errors.Select(e => e.Description))
            );
            return;
        }

        await transaction.CommitAsync();

        await _eventDispatcher.SendAsync(
            new UserCreated(user.Id, user.FirstName + " " + user.LastName, user.PassPortNumber)
        );
    }
}
