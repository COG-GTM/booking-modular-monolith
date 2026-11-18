using System.Security.Claims;
using BuildingBlocks.Constants;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Common;

public class JwtAuthorizationPolicyTests
{
    [Theory]
    [InlineData(IdentityConstant.Role.Admin, null, IdentityConstant.Role.Admin, false)]
    [InlineData(IdentityConstant.Role.User, null, IdentityConstant.Role.User, false)]
    [InlineData(IdentityConstant.Role.Admin, "Bearer", IdentityConstant.Role.Admin, true)]
    [InlineData(IdentityConstant.Role.User, "Bearer", IdentityConstant.Role.User, true)]
    [InlineData(IdentityConstant.Role.Admin, "Bearer", IdentityConstant.Role.User, false)]
    [InlineData(IdentityConstant.Role.User, "Bearer", IdentityConstant.Role.Admin, false)]
    [InlineData(IdentityConstant.Role.Admin, "Bearer", null, false)]
    [InlineData(IdentityConstant.Role.User, "Bearer", null, false)]
    public async Task role_policy_requires_authentication_and_matching_role(
        string policy,
        string authenticationType,
        string role,
        bool expectedSuccess
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddJwt();
        using var provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var claims = role is null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.Role, role) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType));

        var result = await authorization.AuthorizeAsync(principal, null, policy);

        result.Succeeded.Should().Be(expectedSuccess);
    }
}
