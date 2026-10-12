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
    public void get_clients_should_throw_when_client_secret_is_not_configured(string clientSecret)
    {
        var authOptions = new AuthOptions { ClientId = "client", ClientSecret = clientSecret };

        var act = () => Config.GetClients(authOptions).ToList();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(AuthOptions)}:{nameof(AuthOptions.ClientSecret)}*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void get_clients_should_throw_when_client_id_is_not_configured(string clientId)
    {
        var authOptions = new AuthOptions { ClientId = clientId, ClientSecret = "some-secret" };

        var act = () => Config.GetClients(authOptions).ToList();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(AuthOptions)}:{nameof(AuthOptions.ClientId)}*");
    }

    [Fact]
    public void get_clients_should_use_the_configured_client_id_and_hashed_secret()
    {
        var authOptions = new AuthOptions { ClientId = "booking-client", ClientSecret = "configured-secret" };

        var client = Config.GetClients(authOptions).Should().ContainSingle().Subject;

        client.ClientId.Should().Be("booking-client");
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be("configured-secret".Sha256());
    }

    [Fact]
    public void get_clients_should_not_fall_back_to_the_legacy_hardcoded_credentials()
    {
        var authOptions = new AuthOptions { ClientId = "booking-client", ClientSecret = "configured-secret" };

        var client = Config.GetClients(authOptions).Single();

        client.ClientId.Should().NotBe("client");
        client.ClientSecrets.Should().NotContain(secret => secret.Value == "secret".Sha256());
    }

    [Fact]
    public void get_clients_should_keep_the_resource_owner_password_client_configuration()
    {
        var authOptions = new AuthOptions { ClientId = "client", ClientSecret = "configured-secret" };

        var client = Config.GetClients(authOptions).Single();

        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.ResourceOwnerPassword);
        client
            .AllowedScopes.Should()
            .BeEquivalentTo(
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
        client.AccessTokenLifetime.Should().Be(3600);
        client.IdentityTokenLifetime.Should().Be(3600);
        client.AlwaysIncludeUserClaimsInIdToken.Should().BeTrue();
    }

    [Fact]
    public void get_clients_should_be_evaluated_eagerly_so_misconfiguration_fails_at_startup()
    {
        var authOptions = new AuthOptions { ClientId = "client", ClientSecret = null };

        var act = () => Config.GetClients(authOptions);

        act.Should().Throw<InvalidOperationException>();
    }
}
