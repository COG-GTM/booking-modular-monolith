using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityServerConfigurationTests
{
    [Fact]
    public void should_hash_the_configured_client_secret_instead_of_the_old_default()
    {
        var secret = Guid.NewGuid().ToString("N");

        var client = Config.Clients(secret).Should().ContainSingle().Subject;

        client
            .ClientSecrets.Should()
            .ContainSingle()
            .Which.Value.Should()
            .Be(secret.Sha256())
            .And.NotBe("secret".Sha256());
    }

    [Fact]
    public async Task should_resolve_auth_options_and_client_store_with_configured_secret()
    {
        var secret = Guid.NewGuid().ToString("N");
        var builder = CreateBuilder(secret);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<AuthOptions>().ClientSecret.Should().Be(secret);
        var client = await provider.GetRequiredService<IClientStore>().FindClientByIdAsync("client");

        client.Should().NotBeNull();
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(secret.Sha256());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_validate_missing_client_secret_on_startup(string secret)
    {
        var builder = CreateBuilder(secret);
        using var provider = builder.Services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IStartupValidator>().Validate();

        act.Should().Throw<OptionsValidationException>().WithMessage("*ClientSecret*");
    }

    [Fact]
    public void should_bind_identity_seed_options_from_configuration()
    {
        var adminPassword = Guid.NewGuid().ToString("N");
        var userPassword = Guid.NewGuid().ToString("N");
        var builder = CreateBuilder(Guid.NewGuid().ToString("N"), adminPassword, userPassword);
        using var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;

        options.AdminPassword.Should().Be(adminPassword);
        options.UserPassword.Should().Be(userPassword);
    }

    private static WebApplicationBuilder CreateBuilder(
        string secret,
        string adminPassword = null,
        string userPassword = null
    )
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string>
            {
                ["AuthOptions:ClientSecret"] = secret,
                ["IdentitySeedOptions:AdminPassword"] = adminPassword,
                ["IdentitySeedOptions:UserPassword"] = userPassword,
            }
        );
        return builder.AddCustomIdentityServer();
    }
}
