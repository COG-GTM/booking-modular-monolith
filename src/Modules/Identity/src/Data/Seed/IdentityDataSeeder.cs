using System;
using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using Identity.Configurations;
using Identity.Identity.Constants;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
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
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<IdentityDataSeeder> _logger;

    public IdentityDataSeeder(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        IEventDispatcher eventDispatcher,
        IdentityContext identityContext,
        IdentitySeedOptions seedOptions,
        IWebHostEnvironment env,
        ILogger<IdentityDataSeeder> logger
    )
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _eventDispatcher = eventDispatcher;
        _identityContext = identityContext;
        _seedOptions = seedOptions;
        _env = env;
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
            var admin = InitialData.Users.First();

            if (string.IsNullOrWhiteSpace(_seedOptions.AdminPassword))
            {
                _logger.LogWarning(
                    "No {Section}:{Key} configured; skipping initial admin user seed.",
                    nameof(IdentitySeedOptions),
                    nameof(IdentitySeedOptions.AdminPassword)
                );
            }
            else
            {
                await SeedUser(admin, _seedOptions.AdminPassword, IdentityConstant.Role.Admin);
            }

            // demo (non-admin) account is only ever seeded for local development
            if (_env.IsDevelopment() && !string.IsNullOrWhiteSpace(_seedOptions.UserPassword))
            {
                await SeedUser(InitialData.Users.Last(), _seedOptions.UserPassword, IdentityConstant.Role.User);
            }
        }
    }

    private async Task SeedUser(User user, string password, string role)
    {
        if (await _userManager.FindByNameAsync(user.UserName!) != null)
        {
            return;
        }

        var result = await _userManager.CreateAsync(user, password);

        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, role);

            await _eventDispatcher.SendAsync(
                new UserCreated(user.Id, user.FirstName + " " + user.LastName, user.PassPortNumber)
            );
        }
    }
}
