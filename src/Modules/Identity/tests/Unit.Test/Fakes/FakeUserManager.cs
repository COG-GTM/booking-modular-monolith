using Identity.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Unit.Test.Fakes;

public static class FakeUserManager
{
    public static UserManager<User> Create()
    {
        var store = Substitute.For<IUserStore<User>>();
        var userManager = Substitute.For<UserManager<User>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            Substitute.For<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<User>>>()
        );

        userManager.CreateAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);
        userManager.AddToRoleAsync(Arg.Any<User>(), Arg.Any<string>()).Returns(IdentityResult.Success);

        return userManager;
    }
}
