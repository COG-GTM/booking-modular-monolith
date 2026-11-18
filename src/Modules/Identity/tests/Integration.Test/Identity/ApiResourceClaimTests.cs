using FluentAssertions;
using IdentityModel;
using Xunit;

namespace Integration.Test.Identity;

using global::Identity.Configurations;
using global::Identity.Identity.Constants;

public class ApiResourceClaimTests
{
    [Theory]
    [InlineData(Constants.StandardScopes.FlightApi)]
    [InlineData(Constants.StandardScopes.PassengerApi)]
    [InlineData(Constants.StandardScopes.BookingApi)]
    [InlineData(Constants.StandardScopes.IdentityApi)]
    [InlineData(Constants.StandardScopes.BookingModularMonolith)]
    public void should_request_the_role_claim_for_each_api_scope(string scope)
    {
        var resource = Config.ApiResources.Should().ContainSingle(resource => resource.Scopes.Contains(scope)).Which;

        resource.UserClaims.Should().Contain(JwtClaimTypes.Role);
    }
}
