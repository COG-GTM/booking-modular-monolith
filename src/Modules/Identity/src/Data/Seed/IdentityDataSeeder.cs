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
    private readonly IWebHostEnvironment _environment;
    private readonly AuthOptions _authOptions;
    private readonly ILogger<IdentityDataSeeder> _logger;

    public IdentityDataSeeder(
        UserManager<User> userManager,
        RoleManager<Role> roleManager,
        IEventDispatcher eventDispatcher,
        IdentityContext identityContext,
        IWebHostEnvironment environment,
        AuthOptions authOptions,
        ILogger<IdentityDataSeeder> logger
    )
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _eventDispatcher = eventDispatcher;
        _identityContext = identityContext;
        _environment = environment;
        _authOptions = authOptions;
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
        if (!_authOptions.SeedDefaultUsers)
        {
            return;
        }

        if (!_environment.IsDevelopment())
        {
            _logger.LogWarning(
                "{Option} is enabled but the environment is '{Environment}'. Default users are only seeded in Development.",
                $"{nameof(AuthOptions)}:{nameof(AuthOptions.SeedDefaultUsers)}",
                _environment.EnvironmentName
            );
            return;
        }

        if (
            string.IsNullOrWhiteSpace(_authOptions.SeedAdminPassword)
            || string.IsNullOrWhiteSpace(_authOptions.SeedUserPassword)
        )
        {
            _logger.LogWarning(
                "{AdminOption} and {UserOption} must be configured to seed default users. Skipping.",
                $"{nameof(AuthOptions)}:{nameof(AuthOptions.SeedAdminPassword)}",
                $"{nameof(AuthOptions)}:{nameof(AuthOptions.SeedUserPassword)}"
            );
            return;
        }

        if (!await _identityContext.Users.AnyAsync())
        {
            if (await _userManager.FindByNameAsync(InitialData.Users.First().UserName) == null)
            {
                var result = await _userManager.CreateAsync(InitialData.Users.First(), _authOptions.SeedAdminPassword);

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(InitialData.Users.First(), IdentityConstant.Role.Admin);

                    await _eventDispatcher.SendAsync(
                        new UserCreated(
                            InitialData.Users.First().Id,
                            InitialData.Users.First().FirstName + " " + InitialData.Users.First().LastName,
                            InitialData.Users.First().PassPortNumber
                        )
                    );
                }
            }

            if (await _userManager.FindByNameAsync(InitialData.Users.Last().UserName) == null)
            {
                var result = await _userManager.CreateAsync(InitialData.Users.Last(), _authOptions.SeedUserPassword);

                if (result.Succeeded)
                {
                    await _userManager.AddToRoleAsync(InitialData.Users.Last(), IdentityConstant.Role.User);

                    await _eventDispatcher.SendAsync(
                        new UserCreated(
                            InitialData.Users.Last().Id,
                            InitialData.Users.Last().FirstName + " " + InitialData.Users.Last().LastName,
                            InitialData.Users.Last().PassPortNumber
                        )
                    );
                }
            }
        }
    }
}
