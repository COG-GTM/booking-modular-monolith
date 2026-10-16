namespace Unit.Test.Configurations;

using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using FluentAssertions;
using global::Identity.Configurations;
using global::Identity.Identity.Constants;
using IdentityModel;
using Xunit;

public class ConfigTests
{
    [Fact]
    public void identity_resources_should_contain_openid_profile_and_email()
    {
        Config
            .IdentityResources.Select(r => r.Name)
            .Should()
            .BeEquivalentTo(
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                IdentityServerConstants.StandardScopes.Email
            );
    }

    [Fact]
    public void api_scopes_should_contain_the_standard_scopes_and_the_role_scope()
    {
        Config
            .ApiScopes.Select(s => s.Name)
            .Should()
            .BeEquivalentTo(
                Constants.StandardScopes.FlightApi,
                Constants.StandardScopes.PassengerApi,
                Constants.StandardScopes.BookingApi,
                Constants.StandardScopes.IdentityApi,
                Constants.StandardScopes.BookingModularMonolith,
                JwtClaimTypes.Role
            );

        Config.ApiScopes.Single(s => s.Name == JwtClaimTypes.Role).UserClaims.Should().Contain("role");
    }

    [Fact]
    public void api_resources_should_scope_themselves_to_their_own_name()
    {
        Config.ApiResources.Should().HaveCount(5);

        foreach (var resource in Config.ApiResources)
        {
            resource.Scopes.Should().Equal(resource.Name);
        }
    }

    [Fact]
    public void clients_should_contain_a_single_ropc_client()
    {
        var client = Config.Clients.Should().ContainSingle().Which;

        client.ClientId.Should().Be("client");
        client.AllowedGrantTypes.Should().Equal(GrantTypes.ResourceOwnerPassword);
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be("secret".Sha256());
        client.AccessTokenLifetime.Should().Be(3600);
        client.IdentityTokenLifetime.Should().Be(3600);
        client.AlwaysIncludeUserClaimsInIdToken.Should().BeTrue();
    }

    [Fact]
    public void every_client_scope_should_be_a_defined_identity_resource_or_api_scope()
    {
        var definedScopes = Config
            .IdentityResources.Select(r => r.Name)
            .Concat(Config.ApiScopes.Select(s => s.Name))
            .ToHashSet();

        foreach (var client in Config.Clients)
        {
            client.AllowedScopes.Should().OnlyContain(scope => definedScopes.Contains(scope));
        }
    }

    [Fact]
    public void every_api_resource_scope_should_be_a_defined_api_scope()
    {
        var definedScopes = Config.ApiScopes.Select(s => s.Name).ToHashSet();

        foreach (var resource in Config.ApiResources)
        {
            resource.Scopes.Should().OnlyContain(scope => definedScopes.Contains(scope));
        }
    }
}
