using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Identity.Identity.Constants;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityServerClientConfigurationTests
{
    private static readonly string ValidSecret = new('s', ClientOptions.MinimumSecretLength);

    private static ClientOptions MachineClient(string clientId = "booking-api-client") =>
        new()
        {
            ClientId = clientId,
            GrantType = GrantType.ClientCredentials,
            ClientSecret = ValidSecret,
            AllowedScopes = new List<string> { Constants.StandardScopes.BookingModularMonolith },
        };

    private static ClientOptions InteractiveClient(string clientSecret = null) =>
        new()
        {
            ClientId = "spa-client",
            GrantType = GrantType.AuthorizationCode,
            ClientSecret = clientSecret,
            AllowedScopes = new List<string> { "openid", "profile" },
            RedirectUris = new List<string> { "https://localhost:5001/signin-oidc" },
            PostLogoutRedirectUris = new List<string> { "https://localhost:5001/signout-callback-oidc" },
            AllowedCorsOrigins = new List<string> { "https://localhost:5001" },
        };

    private static AuthOptions With(params ClientOptions[] clients) => new() { Clients = clients.ToList() };

    [Fact]
    public void should_register_no_clients_when_none_are_configured()
    {
        Config.GetClients(new AuthOptions()).Should().BeEmpty();
        Config.GetClients(new AuthOptions { Clients = null }).Should().BeEmpty();
        Config.GetClients(null).Should().BeEmpty();
    }

    [Fact]
    public void should_build_a_confidential_client_credentials_client_with_a_hashed_secret()
    {
        var options = MachineClient();
        options.AccessTokenLifetime = 600;
        options.IdentityTokenLifetime = 120;

        var client = Config.GetClients(With(options)).Should().ContainSingle().Subject;

        client.ClientId.Should().Be("booking-api-client");
        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.ClientCredentials);
        client.RequireClientSecret.Should().BeTrue();
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(ValidSecret.Sha256());
        client.ClientSecrets.Should().OnlyContain(x => x.Value != ValidSecret);
        client.AllowedScopes.Should().BeEquivalentTo(Constants.StandardScopes.BookingModularMonolith);
        client.AccessTokenLifetime.Should().Be(600);
        client.IdentityTokenLifetime.Should().Be(120);
        client.AllowOfflineAccess.Should().BeFalse();
        client.AlwaysIncludeUserClaimsInIdToken.Should().BeFalse();
    }

    [Fact]
    public void should_default_to_client_credentials_grant_and_one_hour_access_tokens()
    {
        var options = new ClientOptions();

        options.GrantType.Should().Be(GrantType.ClientCredentials);
        options.AccessTokenLifetime.Should().Be(3600);
        options.IdentityTokenLifetime.Should().Be(300);
        options.AllowedScopes.Should().BeEmpty();
    }

    [Fact]
    public void should_build_a_public_authorization_code_client_with_pkce_when_no_secret_is_configured()
    {
        var client = Config.GetClients(With(InteractiveClient())).Should().ContainSingle().Subject;

        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.Code);
        client.RequirePkce.Should().BeTrue();
        client.AllowPlainTextPkce.Should().BeFalse();
        client.RequireClientSecret.Should().BeFalse();
        client.ClientSecrets.Should().BeEmpty();
        client.RedirectUris.Should().BeEquivalentTo("https://localhost:5001/signin-oidc");
        client.PostLogoutRedirectUris.Should().BeEquivalentTo("https://localhost:5001/signout-callback-oidc");
        client.AllowedCorsOrigins.Should().BeEquivalentTo("https://localhost:5001");
        client.AllowedScopes.Should().BeEquivalentTo("openid", "profile");
    }

    [Fact]
    public void should_build_a_confidential_authorization_code_client_when_a_secret_is_configured()
    {
        var client = Config.GetClients(With(InteractiveClient(ValidSecret))).Should().ContainSingle().Subject;

        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.Code);
        client.RequirePkce.Should().BeTrue();
        client.RequireClientSecret.Should().BeTrue();
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(ValidSecret.Sha256());
    }

    [Fact]
    public void should_ignore_blank_and_duplicate_scopes()
    {
        var options = MachineClient();
        options.AllowedScopes = new List<string>
        {
            Constants.StandardScopes.FlightApi,
            " ",
            null,
            Constants.StandardScopes.FlightApi,
            "openid",
        };

        var client = Config.GetClients(With(options)).Single();

        client.AllowedScopes.Should().BeEquivalentTo(Constants.StandardScopes.FlightApi, "openid");
    }

    [Fact]
    public void should_accept_every_scope_defined_in_identity_resources_and_api_scopes()
    {
        var options = MachineClient();
        options.AllowedScopes = Config
            .IdentityResources.Select(x => x.Name)
            .Concat(Config.ApiScopes.Select(x => x.Name))
            .ToList();

        var client = Config.GetClients(With(options)).Single();

        client.AllowedScopes.Should().BeEquivalentTo(options.AllowedScopes);
    }

    [Fact]
    public void should_tolerate_null_collections_on_a_client()
    {
        var options = MachineClient();
        options.AllowedScopes = null;
        options.RedirectUris = null;
        options.PostLogoutRedirectUris = null;
        options.AllowedCorsOrigins = null;

        var client = Config.GetClients(With(options)).Single();

        client.AllowedScopes.Should().BeEmpty();
        client.RedirectUris.Should().BeEmpty();
        client.PostLogoutRedirectUris.Should().BeEmpty();
        client.AllowedCorsOrigins.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_reject_a_client_without_a_client_id(string clientId)
    {
        var act = () => Config.GetClients(With(MachineClient(clientId)));

        act.Should().Throw<InvalidOperationException>().WithMessage("*must specify a ClientId*");
    }

    [Fact]
    public void should_reject_duplicate_client_ids()
    {
        var act = () => Config.GetClients(With(MachineClient("dup"), MachineClient("dup")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*more than one client with ClientId 'dup'*");
    }

    [Theory]
    [InlineData(GrantType.ResourceOwnerPassword)]
    [InlineData(GrantType.Implicit)]
    [InlineData(GrantType.Hybrid)]
    [InlineData(GrantType.DeviceFlow)]
    [InlineData("urn:ietf:params:oauth:grant-type:token-exchange")]
    [InlineData("")]
    [InlineData(null)]
    public void should_reject_unsupported_grant_types(string grantType)
    {
        var options = MachineClient();
        options.GrantType = grantType;

        var act = () => Config.GetClients(With(options));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*unsupported grant type '{grantType}'*")
            .WithMessage($"*'{GrantType.ClientCredentials}' and '{GrantType.AuthorizationCode}'*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void should_reject_a_client_credentials_client_without_a_secret(string secret)
    {
        var options = MachineClient();
        options.ClientSecret = secret;

        var act = () => Config.GetClients(With(options));

        act.Should().Throw<InvalidOperationException>().WithMessage("*requires a ClientSecret*");
    }

    [Fact]
    public void should_reject_a_secret_shorter_than_the_minimum_length()
    {
        var options = MachineClient();
        options.ClientSecret = new string('s', ClientOptions.MinimumSecretLength - 1);

        var act = () => Config.GetClients(With(options));

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*ClientSecret shorter than {ClientOptions.MinimumSecretLength} characters*");
    }

    [Fact]
    public void should_reject_a_short_secret_on_an_authorization_code_client()
    {
        var act = () => Config.GetClients(With(InteractiveClient("short")));

        act.Should().Throw<InvalidOperationException>().WithMessage("*ClientSecret shorter than*");
    }

    [Fact]
    public void should_reject_an_unknown_scope()
    {
        var options = MachineClient();
        options.AllowedScopes = new List<string> { Constants.StandardScopes.FlightApi, "admin-api" };

        var act = () => Config.GetClients(With(options));

        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown scope 'admin-api'*");
    }

    [Fact]
    public void should_reject_an_authorization_code_client_without_a_redirect_uri()
    {
        var options = InteractiveClient();
        options.RedirectUris = new List<string>();

        var act = () => Config.GetClients(With(options));

        act.Should().Throw<InvalidOperationException>().WithMessage("*must configure at least one RedirectUri*");
    }

    [Fact]
    public void should_not_expose_the_legacy_hardcoded_password_grant_client()
    {
        var clients = Config.GetClients(With(MachineClient())).ToList();

        clients.Should().NotContain(x => x.ClientId == "client");
        clients.Should().NotContain(x => x.ClientSecrets.Any(s => s.Value == "secret".Sha256()));
        clients.Should().NotContain(x => x.AllowedGrantTypes.Contains(GrantType.ResourceOwnerPassword));
    }

    [Fact]
    public async Task should_register_configured_clients_with_identity_server_from_configuration()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string>
            {
                ["AuthOptions:IssuerUri"] = "https://localhost:3000",
                ["AuthOptions:Clients:0:ClientId"] = "booking-api-client",
                ["AuthOptions:Clients:0:GrantType"] = GrantType.ClientCredentials,
                ["AuthOptions:Clients:0:ClientSecret"] = ValidSecret,
                ["AuthOptions:Clients:0:AllowedScopes:0"] = Constants.StandardScopes.BookingModularMonolith,
            }
        );

        builder.AddCustomIdentityServer();

        await using var provider = builder.Services.BuildServiceProvider();
        var clientStore = provider.GetRequiredService<IClientStore>();

        var client = await clientStore.FindClientByIdAsync("booking-api-client");
        client.Should().NotBeNull();
        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.ClientCredentials);
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(ValidSecret.Sha256());
        client.AllowedScopes.Should().BeEquivalentTo(Constants.StandardScopes.BookingModularMonolith);

        (await clientStore.FindClientByIdAsync("client")).Should().BeNull();

        var authOptions = provider.GetRequiredService<AuthOptions>();
        authOptions.Clients.Should().ContainSingle().Which.ClientId.Should().Be("booking-api-client");
    }

    [Fact]
    public async Task should_register_no_identity_server_clients_when_configuration_has_an_empty_list()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string>
            {
                ["AuthOptions:IssuerUri"] = "https://localhost:3000",
                ["AuthOptions:Clients"] = "",
            }
        );

        builder.AddCustomIdentityServer();

        await using var provider = builder.Services.BuildServiceProvider();
        var clientStore = provider.GetRequiredService<IClientStore>();

        (await clientStore.FindClientByIdAsync("client")).Should().BeNull();
        (await clientStore.FindClientByIdAsync("booking-api-client")).Should().BeNull();
    }

    [Fact]
    public void should_fail_fast_at_startup_when_a_configured_client_uses_the_password_grant()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string>
            {
                ["AuthOptions:IssuerUri"] = "https://localhost:3000",
                ["AuthOptions:Clients:0:ClientId"] = "legacy",
                ["AuthOptions:Clients:0:GrantType"] = GrantType.ResourceOwnerPassword,
                ["AuthOptions:Clients:0:ClientSecret"] = ValidSecret,
            }
        );

        var act = () => builder.AddCustomIdentityServer();

        act.Should().Throw<InvalidOperationException>().WithMessage("*unsupported grant type 'password'*");
    }

    [Fact]
    public void should_fail_fast_at_startup_when_a_configured_client_has_no_secret()
    {
        var builder = CreateBuilder(
            new Dictionary<string, string>
            {
                ["AuthOptions:IssuerUri"] = "https://localhost:3000",
                ["AuthOptions:Clients:0:ClientId"] = "booking-api-client",
            }
        );

        var act = () => builder.AddCustomIdentityServer();

        act.Should().Throw<InvalidOperationException>().WithMessage("*requires a ClientSecret*");
    }

    private static WebApplicationBuilder CreateBuilder(IDictionary<string, string> configuration)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );

        builder.Configuration.AddInMemoryCollection(configuration);

        return builder;
    }
}
