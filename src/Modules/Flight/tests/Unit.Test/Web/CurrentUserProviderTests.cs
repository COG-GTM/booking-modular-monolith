using System.Security.Claims;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Unit.Test.Web;

public class CurrentUserProviderTests
{
    private static CurrentUserProvider CreateProvider(params Claim[] claims)
    {
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };

        return new CurrentUserProvider(new HttpContextAccessor { HttpContext = httpContext });
    }

    [Fact]
    public void get_current_user_identifier_should_read_sub_claim()
    {
        // Arrange
        var sub = Guid.NewGuid().ToString();
        var provider = CreateProvider(new Claim(JwtRegisteredClaimNames.Sub, sub));

        // Act
        var identifier = provider.GetCurrentUserIdentifier();

        // Assert
        identifier.Should().Be(sub);
    }

    [Fact]
    public void get_current_user_identifier_should_fall_back_to_name_identifier_claim()
    {
        // Arrange
        var provider = CreateProvider(new Claim(ClaimTypes.NameIdentifier, "42"));

        // Act
        var identifier = provider.GetCurrentUserIdentifier();

        // Assert
        identifier.Should().Be("42");
    }

    [Fact]
    public void get_current_user_identifier_should_prefer_sub_over_name_identifier()
    {
        // Arrange
        var provider = CreateProvider(
            new Claim(ClaimTypes.NameIdentifier, "legacy"),
            new Claim(JwtRegisteredClaimNames.Sub, "subject")
        );

        // Act
        var identifier = provider.GetCurrentUserIdentifier();

        // Assert
        identifier.Should().Be("subject");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void get_current_user_identifier_should_return_null_for_blank_sub(string sub)
    {
        // Arrange
        var provider = CreateProvider(new Claim(JwtRegisteredClaimNames.Sub, sub));

        // Act
        var identifier = provider.GetCurrentUserIdentifier();

        // Assert
        identifier.Should().BeNull();
    }

    [Fact]
    public void get_current_user_identifier_should_return_null_without_claims()
    {
        // Arrange
        var provider = CreateProvider();

        // Act
        var identifier = provider.GetCurrentUserIdentifier();

        // Assert
        identifier.Should().BeNull();
    }

    [Fact]
    public void get_current_user_identifier_should_return_null_without_http_context()
    {
        // Arrange
        var provider = new CurrentUserProvider(new HttpContextAccessor());

        // Act
        var identifier = provider.GetCurrentUserIdentifier();
        var userId = provider.GetCurrentUserId();

        // Assert
        identifier.Should().BeNull();
        userId.Should().BeNull();
    }

    [Fact]
    public void get_current_user_id_should_parse_numeric_sub()
    {
        // Arrange
        var provider = CreateProvider(new Claim(JwtRegisteredClaimNames.Sub, "12345"));

        // Act
        var userId = provider.GetCurrentUserId();

        // Assert
        userId.Should().Be(12345);
    }

    [Fact]
    public void get_current_user_id_should_return_null_for_non_numeric_sub()
    {
        // Arrange
        var provider = CreateProvider(new Claim(JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString()));

        // Act
        var userId = provider.GetCurrentUserId();

        // Assert
        userId.Should().BeNull();
    }
}
