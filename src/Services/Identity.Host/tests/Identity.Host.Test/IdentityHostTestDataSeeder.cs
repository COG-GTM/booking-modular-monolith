using BuildingBlocks.Constants;
using BuildingBlocks.EFCore;
using Identity.Identity.Constants;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace Identity.Host.Test;

// TestFixtureCore resets the Identity DB after every test, so roles and a login user are re-seeded per test.
public class IdentityHostTestDataSeeder(UserManager<User> userManager, RoleManager<Role> roleManager) : ITestDataSeeder
{
    public const string UserName = "host-user";
    public const string Password = "User@123456";

    public async Task SeedAllAsync()
    {
        foreach (var role in new[] { IdentityConstant.Role.Admin, IdentityConstant.Role.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new Role { Name = role });
            }
        }

        if (await userManager.FindByNameAsync(UserName) is null)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = "Host",
                LastName = "User",
                UserName = UserName,
                Email = "host-user@test.com",
                PassPortNumber = "H12345678",
                SecurityStamp = Guid.NewGuid().ToString(),
            };

            await userManager.CreateAsync(user, Password);
            await userManager.AddToRoleAsync(user, IdentityConstant.Role.User);
        }
    }
}
