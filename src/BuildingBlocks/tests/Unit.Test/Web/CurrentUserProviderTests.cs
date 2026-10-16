using System.Security.Claims;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class CurrentUserProviderTests
{
    [Fact]
    public void name_identifier_claim_is_returned_as_the_user_id()
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "42")], "TestAuth")),
        };
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(context);
        var sut = new CurrentUserProvider(accessor);

        sut.GetCurrentUserId().Should().Be(42);
    }
}
