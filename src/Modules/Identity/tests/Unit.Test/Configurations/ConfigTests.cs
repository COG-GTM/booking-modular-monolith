using System;
using System.Linq;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using FluentAssertions;
using Identity.Configurations;
using Identity.Identity.Constants;
using IdentityModel;
using Xunit;

namespace Unit.Test.Configurations;

public class ConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void get_clients_should_fail_closed_when_client_secret_is_not_configured(string clientSecret)
    {
        var authOptions = new AuthOptions { ClientSecret = clientSecret };

        var act = () => Config.GetClients(authOptions).ToList();

        act.Should().Throw<InvalidOperationException>().WithMessage("*AuthOptions:ClientSecret*");
    }

    [Fact]
    public void get_clients_should_fail_closed_when_auth_options_are_missing()
    {
        var act = () => Config.GetClients(null).ToList();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void client_secret_should_come_from_configuration_instead_of_a_hardcoded_literal()
    {
        var configuredSecret = Guid.NewGuid().ToString("N");

        var client = Config.GetClients(new AuthOptions { ClientSecret = configuredSecret }).Single();

        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(configuredSecret.Sha256());
        client.ClientSecrets.Select(x => x.Value).Should().NotContain("secret".Sha256());
    }

    [Fact]
    public void client_should_only_be_allowed_the_scopes_the_api_requires()
    {
        var client = Config.GetClients(new AuthOptions { ClientSecret = Guid.NewGuid().ToString("N") }).Single();

        client.AllowedScopes.Should().BeEquivalentTo(
            IdentityServerConstants.StandardScopes.OpenId,
            IdentityServerConstants.StandardScopes.Profile,
            JwtClaimTypes.Role,
            Constants.StandardScopes.BookingModularMonolith);

        client.AllowedScopes.Should().NotContain(
            new[]
            {
                Constants.StandardScopes.FlightApi,
                Constants.StandardScopes.PassengerApi,
                Constants.StandardScopes.BookingApi,
                Constants.StandardScopes.IdentityApi,
            });
    }
}
