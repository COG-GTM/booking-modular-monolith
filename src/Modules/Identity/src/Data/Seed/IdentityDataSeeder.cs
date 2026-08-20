using System;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using Identity.Configurations;
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

    private async Task SeedUsers()
    {
        if (!await _identityContext.Users.AnyAsync())
        {
            if (string.IsNullOrWhiteSpace(_seedOptions.AdminPassword))
            {
                _logger.LogWarning(
                    "Skipped seeding admin user: 'IdentitySeedOptions:AdminPassword' is not configured.");
            }
            else if (await _userManager.FindByNameAsync("samh") == null)
            {
                var result = await _userManager.CreateAsync(InitialData.Users.First(), _seedOptions.AdminPassword);

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(InitialData.Users.First(), IdentityConstant.Role.Admin);

                    await _eventDispatcher.SendAsync(
                        new UserCreated(
                            InitialData.Users.First().Id,
                            InitialData.Users.First().FirstName +
                            " " +
                            InitialData.Users.First().LastName,
                            InitialData.Users.First().PassPortNumber));
                }
            }

            if (string.IsNullOrWhiteSpace(_seedOptions.UserPassword))
            {
                _logger.LogWarning(
                    "Skipped seeding user: 'IdentitySeedOptions:UserPassword' is not configured.");
            }
            else if (await _userManager.FindByNameAsync("meysamh2") == null)
            {
                var result = await _userManager.CreateAsync(InitialData.Users.Last(), _seedOptions.UserPassword);

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(InitialData.Users.Last(), IdentityConstant.Role.User);

                    await _eventDispatcher.SendAsync(
                        new UserCreated(
                            InitialData.Users.Last().Id,
                            InitialData.Users.Last().FirstName +
                            " " +
                            InitialData.Users.Last().LastName,
                            InitialData.Users.Last().PassPortNumber));
                }
            }
        }
    }
}