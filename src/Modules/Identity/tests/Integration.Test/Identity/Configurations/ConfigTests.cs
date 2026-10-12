using System.Linq;
using FluentAssertions;
using Identity.Configurations;
using Identity.Identity.Constants;
using IdentityModel;
using Xunit;

namespace Integration.Test.Identity.Configurations;

public class ConfigTests
{
    [Fact]
    public void booking_modular_monolith_api_resource_should_include_role_user_claim()
    {
        // Act
        var apiResource = Config.ApiResources
            .Single(x => x.Name == Constants.StandardScopes.BookingModularMonolith);

        // Assert
        apiResource.Scopes.Should().Contain(Constants.StandardScopes.BookingModularMonolith);
        apiResource.UserClaims.Should().Contain(JwtClaimTypes.Role);
    }

    [Fact]
    public void other_api_resources_should_not_be_changed_to_include_role_user_claim()
    {
        // Act
        var otherResources = Config.ApiResources
            .Where(x => x.Name != Constants.StandardScopes.BookingModularMonolith)
            .ToList();

        // Assert
        otherResources.Should().HaveCount(4);
        otherResources.Should().OnlyContain(x => !x.UserClaims.Contains(JwtClaimTypes.Role));
    }

    [Fact]
    public void ropc_client_should_be_allowed_to_request_booking_modular_monolith_scope()
    {
        // Act
        var client = Config.Clients.Single(x => x.ClientId == "client");

        // Assert
        client.AllowedScopes.Should().Contain(Constants.StandardScopes.BookingModularMonolith);
    }
}
