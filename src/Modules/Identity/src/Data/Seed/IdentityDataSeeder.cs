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
using Microsoft.Extensions.Options;

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
        IOptions<IdentitySeedOptions> seedOptions,
        ILogger<IdentityDataSeeder> logger
    )
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _eventDispatcher = eventDispatcher;
        _identityContext = identityContext;
        _seedOptions = seedOptions.Value;
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
            if (await _userManager.FindByNameAsync(admin.UserName) == null)
            {
                if (string.IsNullOrWhiteSpace(_seedOptions.AdminPassword))
                {
                    _logger.LogWarning(
                        "IdentitySeedOptions:AdminPassword is not configured; skipping seeding of user {UserName}",
                        admin.UserName
                    );
                }
                else
                {
                    var result = await _userManager.CreateAsync(admin, _seedOptions.AdminPassword);

                    if (result.Succeeded)
                    {
                        await _userManager.AddToRoleAsync(admin, IdentityConstant.Role.Admin);

                        await _eventDispatcher.SendAsync(
                            new UserCreated(admin.Id, admin.FirstName + " " + admin.LastName, admin.PassPortNumber)
                        );
                    }
                }
            }

            var user = InitialData.Users.Last();
            if (await _userManager.FindByNameAsync(user.UserName) == null)
            {
                if (string.IsNullOrWhiteSpace(_seedOptions.UserPassword))
                {
                    _logger.LogWarning(
                        "IdentitySeedOptions:UserPassword is not configured; skipping seeding of user {UserName}",
                        user.UserName
                    );
                }
                else
                {
                    var result = await _userManager.CreateAsync(user, _seedOptions.UserPassword);

                    if (result.Succeeded)
                    {
                        await _userManager.AddToRoleAsync(user, IdentityConstant.Role.User);

                        await _eventDispatcher.SendAsync(
                            new UserCreated(user.Id, user.FirstName + " " + user.LastName, user.PassPortNumber)
                        );
                    }
                }
            }
        }
    }
}
