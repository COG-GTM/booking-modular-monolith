namespace Identity.Configurations;

using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Validation;
using Identity.Models;
using Microsoft.AspNetCore.Identity;

public class UserValidator : IResourceOwnerPasswordValidator
{
    private static readonly Lazy<string> DummyPasswordHash = new(() =>
        new PasswordHasher<User>().HashPassword(new User(), Guid.NewGuid().ToString()));

    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;

    public UserValidator(SignInManager<User> signInManager,
        UserManager<User> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    public async Task ValidateAsync(ResourceOwnerPasswordValidationContext context)
    {
        var user = string.IsNullOrEmpty(context.UserName)
            ? null
            : await _userManager.FindByNameAsync(context.UserName);

        if (user is null)
        {
            // Spend comparable hashing time so unknown usernames are not distinguishable by latency.
            _userManager.PasswordHasher.VerifyHashedPassword(new User(), DummyPasswordHash.Value, context.Password ?? string.Empty);

            SetInvalidCredentials(context);
            return;
        }

        var signIn = await _signInManager.CheckPasswordSignInAsync(
            user,
            context.Password,
            lockoutOnFailure: true);

        if (signIn.Succeeded)
        {
            var userId = user.Id.ToString();

            // context set to success
            context.Result = new GrantValidationResult(
                subject: userId,
                authenticationMethod: "custom",
                claims: new Claim[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, user.UserName)
                }
            );

            return;
        }

        SetInvalidCredentials(context);
    }

    private static void SetInvalidCredentials(ResourceOwnerPasswordValidationContext context)
    {
        context.Result = new GrantValidationResult(
            TokenRequestErrors.UnauthorizedClient, "Invalid Credentials");
    }
}
