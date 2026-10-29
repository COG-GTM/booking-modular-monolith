using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using Identity.Data.Seed;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;

namespace Host.Test;

public class IdentityHostTestDataSeeder(
    UserManager<User> userManager,
    RoleManager<Role> roleManager,
    IEventDispatcher<global::Identity.IdentityRoot> eventDispatcher
) : ITestDataSeeder
{
    public async Task SeedAllAsync()
    {
        if (!await roleManager.RoleExistsAsync(IdentityConstant.Role.Admin))
            await roleManager.CreateAsync(new Role { Name = IdentityConstant.Role.Admin });

        if (!await roleManager.RoleExistsAsync(IdentityConstant.Role.User))
            await roleManager.CreateAsync(new Role { Name = IdentityConstant.Role.User });

        if (await userManager.FindByNameAsync("samh") == null)
        {
            var user = InitialData.Users.First();
            var result = await userManager.CreateAsync(user, "Admin@123456");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, IdentityConstant.Role.Admin);
                await PublishUserCreated(user);
            }
        }

        if (await userManager.FindByNameAsync("meysamh2") == null)
        {
            var user = InitialData.Users.Last();
            var result = await userManager.CreateAsync(user, "User@123456");
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(user, IdentityConstant.Role.User);
                await PublishUserCreated(user);
            }
        }
    }

    private Task PublishUserCreated(User user) =>
        eventDispatcher.SendAsync(new UserCreated(user.Id, $"{user.FirstName} {user.LastName}", user.PassPortNumber));
}
