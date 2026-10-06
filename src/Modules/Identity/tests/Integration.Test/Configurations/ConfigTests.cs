using System.Linq;
using FluentAssertions;
using Identity.Configurations;
using Identity.Identity.Constants;
using IdentityModel;
using Xunit;

namespace Integration.Test.Configurations;

public class ConfigTests
{
    [Fact]
    public void booking_modular_monolith_api_resource_should_include_role_user_claim()
    {
        // Act
        var resource = Config.ApiResources.Single(x => x.Name == Constants.StandardScopes.BookingModularMonolith);

        // Assert
        resource.Scopes.Should().Contain(Constants.StandardScopes.BookingModularMonolith);
        resource.UserClaims.Should().Contain(JwtClaimTypes.Role);
    }
}
