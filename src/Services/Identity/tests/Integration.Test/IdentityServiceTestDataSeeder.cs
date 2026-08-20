using BuildingBlocks.Constants;
using BuildingBlocks.EFCore;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace Integration.Test;

public class IdentityServiceTestDataSeeder(RoleManager<Role> roleManager) : ITestDataSeeder
{
    public async Task SeedAllAsync()
    {
        if (await roleManager.RoleExistsAsync(IdentityConstant.Role.Admin) == false)
        {
            await roleManager.CreateAsync(new Role { Name = IdentityConstant.Role.Admin });
        }

        if (await roleManager.RoleExistsAsync(IdentityConstant.Role.User) == false)
        {
            await roleManager.CreateAsync(new Role { Name = IdentityConstant.Role.User });
        }
    }
}
