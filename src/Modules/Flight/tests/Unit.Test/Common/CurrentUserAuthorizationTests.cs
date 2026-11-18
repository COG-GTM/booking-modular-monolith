using System.Net;
using System.Security.Claims;
using BuildingBlocks.Constants;
using BuildingBlocks.Exception;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Unit.Test.Common;

public class CurrentUserAuthorizationTests
{
    [Fact]
    public void admin_should_bypass_ownership_even_without_subject_or_owner()
    {
        var provider = CreateProvider(new Claim(ClaimTypes.Role, IdentityConstant.Role.Admin));
        var act = () => provider.EnsureOwnerOrAdmin(null);
        act.Should().NotThrow();
    }

    [Fact]
    public void matching_owner_should_be_allowed()
    {
        var owner = Guid.NewGuid();
        var provider = CreateProvider(new Claim("sub", owner.ToString()));
        var act = () => provider.EnsureOwnerOrAdmin(owner);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void missing_or_mismatched_ownership_should_be_forbidden(bool hasSubject, bool hasOwner)
    {
        var claims = hasSubject ? new[] { new Claim("sub", Guid.NewGuid().ToString()) } : Array.Empty<Claim>();
        var provider = CreateProvider(claims);
        var act = () => provider.EnsureOwnerOrAdmin(hasOwner ? Guid.NewGuid() : null);
        act.Should().Throw<ForbiddenException>();
    }

    [Fact]
    public void subject_should_take_precedence_over_name_identifier()
    {
        var subject = Guid.NewGuid();
        var provider = CreateProvider(
            new Claim("sub", subject.ToString()),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        );
        provider.GetCurrentUserSubjectId().Should().Be(subject);
    }

    [Fact]
    public void name_identifier_should_be_used_when_subject_is_absent()
    {
        var subject = Guid.NewGuid();
        var provider = CreateProvider(new Claim(ClaimTypes.NameIdentifier, subject.ToString()));
        provider.GetCurrentUserSubjectId().Should().Be(subject);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public void invalid_subject_should_return_null_even_with_valid_fallback(string subject)
    {
        var provider = CreateProvider(
            new Claim("sub", subject),
            new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())
        );
        provider.GetCurrentUserSubjectId().Should().BeNull();
    }

    [Fact]
    public void absent_claims_or_http_context_should_return_no_subject_and_no_role()
    {
        CreateProvider().GetCurrentUserSubjectId().Should().BeNull();
        CreateProvider().IsInRole(IdentityConstant.Role.Admin).Should().BeFalse();
        var provider = new CurrentUserProvider(new HttpContextAccessor());
        provider.GetCurrentUserSubjectId().Should().BeNull();
        provider.IsInRole(IdentityConstant.Role.Admin).Should().BeFalse();
    }

    [Fact]
    public void role_check_should_only_match_assigned_role()
    {
        var provider = CreateProvider(new Claim(ClaimTypes.Role, IdentityConstant.Role.User));
        provider.IsInRole(IdentityConstant.Role.User).Should().BeTrue();
        provider.IsInRole(IdentityConstant.Role.Admin).Should().BeFalse();
    }

    [Fact]
    public void forbidden_exception_should_preserve_message_code_and_http_status()
    {
        var exception = new ForbiddenException("Denied", 42);
        exception.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        exception.Message.Should().Be("Denied");
        exception.Code.Should().Be(42);
        new ForbiddenException("Denied").Code.Should().BeNull();
    }

    private static CurrentUserProvider CreateProvider(params Claim[] claims) =>
        new(
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) },
            }
        );
}
