using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using BuildingBlocks.Constants;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace Unit.Test.Common;

public class CurrentUserProviderTests
{
    private static CurrentUserProvider CreateProvider(ClaimsPrincipal? user)
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext = user is null ? null : new DefaultHttpContext { User = user };

        return new CurrentUserProvider(httpContextAccessor);
    }

    private static ClaimsPrincipal CreatePrincipal(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test", ClaimTypes.Name, ClaimTypes.Role));
    }

    [Fact]
    public void get_current_user_guid_should_read_sub_claim()
    {
        var userId = Guid.NewGuid();
        var provider = CreateProvider(CreatePrincipal(new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())));

        provider.GetCurrentUserGuid().Should().Be(userId);
    }

    [Fact]
    public void get_current_user_guid_should_fall_back_to_name_identifier_claim()
    {
        var userId = Guid.NewGuid();
        var provider = CreateProvider(CreatePrincipal(new Claim(ClaimTypes.NameIdentifier, userId.ToString())));

        provider.GetCurrentUserGuid().Should().Be(userId);
    }

    [Fact]
    public void get_current_user_guid_should_prefer_sub_over_name_identifier()
    {
        var subject = Guid.NewGuid();
        var nameIdentifier = Guid.NewGuid();
        var provider = CreateProvider(
            CreatePrincipal(
                new Claim(ClaimTypes.NameIdentifier, nameIdentifier.ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, subject.ToString())
            )
        );

        provider.GetCurrentUserGuid().Should().Be(subject);
    }

    [Fact]
    public void get_current_user_guid_should_return_null_when_subject_is_not_a_guid()
    {
        var provider = CreateProvider(CreatePrincipal(new Claim(JwtRegisteredClaimNames.Sub, "12345")));

        provider.GetCurrentUserGuid().Should().BeNull();
    }

    [Fact]
    public void get_current_user_guid_should_return_null_when_user_has_no_identity_claims()
    {
        var provider = CreateProvider(new ClaimsPrincipal());

        provider.GetCurrentUserGuid().Should().BeNull();
    }

    [Fact]
    public void get_current_user_guid_should_return_null_when_there_is_no_http_context()
    {
        var provider = CreateProvider(null);

        provider.GetCurrentUserGuid().Should().BeNull();
    }

    [Fact]
    public void is_in_role_should_return_true_when_user_has_the_role()
    {
        var provider = CreateProvider(CreatePrincipal(new Claim(ClaimTypes.Role, IdentityConstant.Role.Admin)));

        provider.IsInRole(IdentityConstant.Role.Admin).Should().BeTrue();
    }

    [Fact]
    public void is_in_role_should_return_false_when_user_has_a_different_role()
    {
        var provider = CreateProvider(CreatePrincipal(new Claim(ClaimTypes.Role, IdentityConstant.Role.User)));

        provider.IsInRole(IdentityConstant.Role.Admin).Should().BeFalse();
    }

    [Fact]
    public void is_in_role_should_return_false_when_there_is_no_http_context()
    {
        var provider = CreateProvider(null);

        provider.IsInRole(IdentityConstant.Role.Admin).Should().BeFalse();
    }

    [Fact]
    public void get_current_user_id_should_remain_unchanged_for_numeric_name_identifier()
    {
        var provider = CreateProvider(CreatePrincipal(new Claim(ClaimTypes.NameIdentifier, "42")));

        provider.GetCurrentUserId().Should().Be(42);
        provider.GetCurrentUserGuid().Should().BeNull();
    }
}
