using System.Security.Claims;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Integration.Test;

public class CurrentUserProviderTests
{
    private const string Subject = "a39c4a51-2079-4d84-b74e-e2349d251cf0";
    private const string OtherSubject = "d66f03ad-30d2-47e0-919a-627461a6e6fb";

    [Theory]
    [InlineData(Subject, null, Subject)]
    [InlineData(null, Subject, Subject)]
    [InlineData(Subject, OtherSubject, Subject)]
    [InlineData(null, null, null)]
    [InlineData("invalid", null, null)]
    [InlineData(null, "123", null)]
    [InlineData("", Subject, null)]
    [InlineData("invalid", Subject, null)]
    public void should_resolve_guid_subject_with_sub_taking_precedence(
        string? subject,
        string? nameIdentifier,
        string? expected
    )
    {
        var claims = new List<Claim>();
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }

        if (nameIdentifier is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, nameIdentifier));
        }

        var provider = CreateProvider(new ClaimsIdentity(claims, "Test"));

        Assert.Equal(expected is null ? null : Guid.Parse(expected), provider.GetCurrentUserSubjectId());
    }

    [Theory]
    [InlineData("role", "admin", true)]
    [InlineData(ClaimTypes.Role, "admin", true)]
    [InlineData("custom-role", "admin", true)]
    [InlineData("role", "user", false)]
    [InlineData("role", "Admin", false)]
    [InlineData("unrelated", "admin", false)]
    public void should_recognize_only_matching_role_claims(string claimType, string value, bool expected)
    {
        var identity = new ClaimsIdentity(
            new[] { new Claim(claimType, value) },
            "Test",
            ClaimTypes.Name,
            "custom-role"
        );
        var provider = CreateProvider(identity);

        Assert.Equal(expected, provider.IsCurrentUserInRole("admin"));
    }

    [Fact]
    public void should_fail_closed_without_an_http_context()
    {
        var provider = new CurrentUserProvider(new HttpContextAccessor());

        Assert.Null(provider.GetCurrentUserSubjectId());
        Assert.False(provider.IsCurrentUserInRole("admin"));
    }

    private static CurrentUserProvider CreateProvider(ClaimsIdentity identity) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } });
}
