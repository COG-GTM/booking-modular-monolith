using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duende.IdentityServer;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Identity.Identity.Constants;
using IdentityModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Extensions;

public class IdentityServerExtensionsTests
{
    private const string ClientId = "client";

    [Fact]
    public void startup_validation_should_fail_when_client_secret_is_not_configured()
    {
        using var provider = BuildServiceProvider(clientSecret: null);

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should()
            .ContainSingle(x => x.Contains(nameof(AuthOptions.ClientSecret)));
    }

    [Fact]
    public void startup_validation_should_pass_when_client_secret_is_configured()
    {
        using var provider = BuildServiceProvider(clientSecret: Guid.NewGuid().ToString("N"));

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().NotThrow();
    }

    [Fact]
    public void clients_should_not_resolve_when_client_secret_is_not_configured()
    {
        using var provider = BuildServiceProvider(clientSecret: null);

        var act = () => provider.GetRequiredService<IEnumerable<Client>>().ToList();

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void clients_should_be_built_from_the_configured_secret()
    {
        var configuredSecret = Guid.NewGuid().ToString("N");
        using var provider = BuildServiceProvider(configuredSecret);

        var client = provider.GetRequiredService<IEnumerable<Client>>().Single();

        client.ClientId.Should().Be(ClientId);
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(configuredSecret.Sha256());
        client
            .AllowedScopes.Should()
            .BeEquivalentTo(
                IdentityServerConstants.StandardScopes.OpenId,
                IdentityServerConstants.StandardScopes.Profile,
                JwtClaimTypes.Role,
                Constants.StandardScopes.BookingModularMonolith
            );
    }

    [Fact]
    public void clients_should_be_registered_as_a_singleton()
    {
        using var provider = BuildServiceProvider(Guid.NewGuid().ToString("N"));

        var first = provider.GetRequiredService<IEnumerable<Client>>();
        var second = provider.GetRequiredService<IEnumerable<Client>>();

        second.Should().BeSameAs(first);
    }

    [Fact]
    public async Task in_memory_client_store_should_find_the_configured_client()
    {
        var configuredSecret = Guid.NewGuid().ToString("N");
        using var provider = BuildServiceProvider(configuredSecret);

        var client = await provider.GetRequiredService<InMemoryClientStore>().FindClientByIdAsync(ClientId);

        client.Should().NotBeNull();
        client!.ClientSecrets.Select(x => x.Value).Should().ContainSingle(configuredSecret.Sha256());
        client.ClientSecrets.Select(x => x.Value).Should().NotContain("secret".Sha256());
    }

    [Fact]
    public async Task in_memory_client_store_should_not_find_unknown_clients()
    {
        using var provider = BuildServiceProvider(Guid.NewGuid().ToString("N"));

        var client = await provider.GetRequiredService<InMemoryClientStore>().FindClientByIdAsync("unknown-client");

        client.Should().BeNull();
    }

    private static ServiceProvider BuildServiceProvider(string? clientSecret)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.Sources.Clear();

        var settings = new Dictionary<string, string> { ["AuthOptions:IssuerUri"] = "https://localhost:3000" };

        if (clientSecret is not null)
        {
            settings["AuthOptions:ClientSecret"] = clientSecret;
        }

        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddCustomIdentityServer();

        return builder.Services.BuildServiceProvider();
    }
}
