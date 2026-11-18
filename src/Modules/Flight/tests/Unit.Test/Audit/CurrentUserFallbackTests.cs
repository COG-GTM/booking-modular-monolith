using System.Security.Claims;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Unit.Test.Audit;

public class CurrentUserFallbackTests
{
    [Fact]
    public void mapped_name_identifier_should_supply_missing_subject()
    {
        var id = Guid.NewGuid();

        Provider(new Claim(ClaimTypes.NameIdentifier, id.ToString())).GetCurrentUserId().Should().Be(id);
    }

    [Fact]
    public void subject_should_take_precedence_over_mapped_identifier()
    {
        var id = Guid.NewGuid();

        Provider(new Claim("sub", id.ToString()), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()))
            .GetCurrentUserId()
            .Should()
            .Be(id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public void invalid_subject_should_not_fall_back_to_another_identity(string subject)
    {
        Provider(new Claim("sub", subject), new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()))
            .GetCurrentUserId()
            .Should()
            .BeNull();
    }

    private static CurrentUserProvider Provider(params Claim[] claims) =>
        new(
            new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")),
                },
            }
        );
}
