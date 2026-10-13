using System.Threading.Tasks;
using BuildingBlocks.Constants;
using BuildingBlocks.EFCore;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.Api.Test;

// The fixture resets the write database after every test, so the roles the register-user flow depends on are re-seeded here.
public class IdentityApiTestDataSeeder(RoleManager<Role> roleManager) : ITestDataSeeder
{
    public async Task SeedAllAsync()
    {
        foreach (var role in new[] { IdentityConstant.Role.Admin, IdentityConstant.Role.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new Role { Name = role });
            }
        }
    }
}
