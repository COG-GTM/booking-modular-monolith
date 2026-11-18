using System;
using System.Security.Claims;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Unit.Test.Common;

public class CurrentUserProviderTests
{
    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public void should_read_guid_from_subject_or_legacy_claim(string claimType)
    {
        var userId = Guid.NewGuid();
        var provider = Create(new Claim(claimType, userId.ToString()));

        provider.GetCurrentUserGuid().Should().Be(userId);
    }

    [Fact]
    public void should_prefer_subject_over_name_identifier()
    {
        var userId = Guid.NewGuid();
        var provider = Create(
            new Claim("sub", userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        );

        provider.GetCurrentUserGuid().Should().Be(userId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("42")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void should_not_fall_back_when_subject_is_invalid(string subject)
    {
        var provider = Create(
            new Claim("sub", subject),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        );

        provider.GetCurrentUserGuid().Should().BeNull();
    }

    [Fact]
    public void should_return_null_without_claims_or_http_context()
    {
        Create().GetCurrentUserGuid().Should().BeNull();
        new CurrentUserProvider(new HttpContextAccessor()).GetCurrentUserGuid().Should().BeNull();
    }

    [Theory]
    [InlineData("sub")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public void should_preserve_numeric_user_id_support(string claimType)
    {
        Create(new Claim(claimType, "42")).GetCurrentUserId().Should().Be(42);
        Create(new Claim(claimType, "invalid")).GetCurrentUserId().Should().BeNull();
        Create().GetCurrentUserId().Should().BeNull();
    }

    private static CurrentUserProvider Create(params Claim[] claims) =>
        new(
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
            }
        );
}
