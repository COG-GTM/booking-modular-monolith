using System.Linq;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using FluentAssertions;
using Identity.Configurations;
using Identity.Identity.Constants;
using IdentityModel;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityClientConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void get_clients_should_return_empty_when_client_secret_is_missing(string? clientSecret)
    {
        Config.GetClients(clientSecret!).Should().BeEmpty();
    }

    [Fact]
    public void get_clients_should_register_one_password_client_when_secret_is_configured()
    {
        const string clientSecret = "s3cret-from-config";

        var client = Config.GetClients(clientSecret).Should().ContainSingle().Subject;

        client.ClientId.Should().Be("client");
        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.ResourceOwnerPassword);
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(clientSecret.Sha256());
        client.ClientSecrets.Select(secret => secret.Value).Should().NotContain("secret".Sha256());
        client
            .AllowedScopes.Should()
            .Contain(
                new[]
                {
                    IdentityServerConstants.StandardScopes.OpenId,
                    IdentityServerConstants.StandardScopes.Profile,
                    JwtClaimTypes.Role,
                    Constants.StandardScopes.FlightApi,
                    Constants.StandardScopes.PassengerApi,
                    Constants.StandardScopes.BookingApi,
                    Constants.StandardScopes.IdentityApi,
                    Constants.StandardScopes.BookingModularMonolith,
                }
            );
    }
}
