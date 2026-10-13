using System.Linq;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using FluentAssertions;
using Identity.Configurations;
using IdentityModel;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityClientConfigTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void has_client_credentials_should_be_false_when_client_secret_is_missing(string? clientSecret)
    {
        var authOptions = new AuthOptions { ClientSecret = clientSecret };

        authOptions.HasClientCredentials.Should().BeFalse();
    }

    [Fact]
    public void has_client_credentials_should_be_true_when_client_secret_is_configured()
    {
        var authOptions = new AuthOptions { ClientSecret = "s3cret-from-config" };

        authOptions.HasClientCredentials.Should().BeTrue();
    }

    [Fact]
    public void client_id_should_default_to_client()
    {
        new AuthOptions().ClientId.Should().Be("client");
    }

    [Fact]
    public void get_clients_should_return_no_clients_when_auth_options_is_null()
    {
        Config.GetClients(null!).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void get_clients_should_return_no_clients_when_client_secret_is_missing(string? clientSecret)
    {
        var authOptions = new AuthOptions { ClientSecret = clientSecret };

        Config.GetClients(authOptions).Should().BeEmpty();
    }

    [Fact]
    public void get_clients_should_register_single_password_client_from_configured_credentials()
    {
        var authOptions = new AuthOptions { ClientId = "booking-cli", ClientSecret = "s3cret-from-config" };

        var clients = Config.GetClients(authOptions).ToList();

        var client = clients.Should().ContainSingle().Subject;
        client.ClientId.Should().Be("booking-cli");
        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.ResourceOwnerPassword);
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be("s3cret-from-config".Sha256());
        client
            .AllowedScopes.Should()
            .Contain(
                new[]
                {
                    IdentityServerConstants.StandardScopes.OpenId,
                    IdentityServerConstants.StandardScopes.Profile,
                    JwtClaimTypes.Role,
                    "flight-api",
                    "passenger-api",
                    "booking-api",
                    "identity-api",
                    "booking-modular-monolith",
                }
            );
    }

    [Fact]
    public void get_clients_should_not_accept_the_previously_hardcoded_secret()
    {
        var authOptions = new AuthOptions { ClientSecret = "s3cret-from-config" };

        var client = Config.GetClients(authOptions).Single();

        client.ClientSecrets.Select(s => s.Value).Should().NotContain("secret".Sha256());
    }

    [Fact]
    public void get_clients_should_use_default_client_id_when_not_overridden()
    {
        var authOptions = new AuthOptions { ClientSecret = "s3cret-from-config" };

        Config.GetClients(authOptions).Single().ClientId.Should().Be("client");
    }
}
