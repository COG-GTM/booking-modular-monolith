using System;
using System.Security.Claims;
using BuildingBlocks.Constants;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Integration.Test.Booking.Authorization;

public class CurrentUserProviderTests
{
    [Fact]
    public void get_current_user_guid_reads_the_sub_claim()
    {
        var userId = Guid.NewGuid();

        var provider = CreateProvider(new Claim("sub", userId.ToString()));

        provider.GetCurrentUserGuid().Should().Be(userId);
    }

    [Fact]
    public void get_current_user_guid_prefers_sub_over_name_identifier()
    {
        var sub = Guid.NewGuid();

        var provider = CreateProvider(
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()),
            new Claim("sub", sub.ToString())
        );

        provider.GetCurrentUserGuid().Should().Be(sub);
    }

    [Fact]
    public void get_current_user_guid_falls_back_to_name_identifier()
    {
        var userId = Guid.NewGuid();

        var provider = CreateProvider(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));

        provider.GetCurrentUserGuid().Should().Be(userId);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("12345")]
    [InlineData("")]
    public void get_current_user_guid_returns_null_for_non_guid_subject(string subject)
    {
        var provider = CreateProvider(new Claim("sub", subject));

        provider.GetCurrentUserGuid().Should().BeNull();
    }

    [Fact]
    public void get_current_user_guid_returns_null_without_user_claims()
    {
        CreateProvider().GetCurrentUserGuid().Should().BeNull();
    }

    [Fact]
    public void get_current_user_guid_and_is_in_role_handle_missing_http_context()
    {
        var provider = new CurrentUserProvider(new HttpContextAccessor());

        provider.GetCurrentUserGuid().Should().BeNull();
        provider.IsInRole(IdentityConstant.Role.Admin).Should().BeFalse();
    }

    [Fact]
    public void is_in_role_uses_the_jwt_role_claim_type()
    {
        // JwtExtensions sets RoleClaimType = "role" and MapInboundClaims = false.
        var identity = new ClaimsIdentity(
            new[] { new Claim("sub", Guid.NewGuid().ToString()), new Claim("role", IdentityConstant.Role.Admin) },
            "Bearer",
            "name",
            "role"
        );
        var provider = CreateProvider(identity);

        provider.IsInRole(IdentityConstant.Role.Admin).Should().BeTrue();
        provider.IsInRole(IdentityConstant.Role.User).Should().BeFalse();
    }

    [Fact]
    public void is_in_role_returns_false_for_a_user_without_the_role()
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim("role", IdentityConstant.Role.User) },
            "Bearer",
            "name",
            "role"
        );

        CreateProvider(identity).IsInRole(IdentityConstant.Role.Admin).Should().BeFalse();
    }

    private static CurrentUserProvider CreateProvider(params Claim[] claims) =>
        CreateProvider(new ClaimsIdentity(claims, "Test"));

    private static CurrentUserProvider CreateProvider(ClaimsIdentity identity) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } });
}
