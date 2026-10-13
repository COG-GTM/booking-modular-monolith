using System.Collections.Generic;
using System.Threading.Tasks;
using BuildingBlocks.Web;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityServerClientRegistrationTests
{
    [Fact]
    public async Task should_register_no_identity_server_client_when_client_secret_is_not_configured()
    {
        await using var app = BuildApp(clientSecret: null);

        var clientStore = app.Services.GetRequiredService<IClientStore>();

        (await clientStore.FindClientByIdAsync("client")).Should().BeNull();
    }

    [Fact]
    public async Task should_register_identity_server_client_from_configured_secret()
    {
        await using var app = BuildApp(clientSecret: "s3cret-from-config");

        var clientStore = app.Services.GetRequiredService<IClientStore>();
        var client = await clientStore.FindClientByIdAsync("client");

        client.Should().NotBeNull();
        client!.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be("s3cret-from-config".Sha256());
        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.ResourceOwnerPassword);
    }

    [Fact]
    public async Task should_register_identity_server_client_under_configured_client_id()
    {
        await using var app = BuildApp(clientSecret: "s3cret-from-config", clientId: "booking-cli");

        var clientStore = app.Services.GetRequiredService<IClientStore>();

        (await clientStore.FindClientByIdAsync("booking-cli")).Should().NotBeNull();
        (await clientStore.FindClientByIdAsync("client")).Should().BeNull();
    }

    [Fact]
    public void should_bind_identity_seed_options_from_configuration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["IdentitySeedOptions:AdminPassword"] = "admin-pass-from-config",
                        ["IdentitySeedOptions:UserPassword"] = "user-pass-from-config",
                    }
                )
                .Build()
        );

        services.AddValidateOptions<IdentitySeedOptions>();

        using var provider = services.BuildServiceProvider();
        var seedOptions = provider.GetRequiredService<IdentitySeedOptions>();

        seedOptions.AdminPassword.Should().Be("admin-pass-from-config");
        seedOptions.UserPassword.Should().Be("user-pass-from-config");
    }

    [Fact]
    public void identity_seed_options_should_have_no_default_passwords()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddValidateOptions<IdentitySeedOptions>();

        using var provider = services.BuildServiceProvider();
        var seedOptions = provider.GetRequiredService<IdentitySeedOptions>();

        seedOptions.AdminPassword.Should().BeNull();
        seedOptions.UserPassword.Should().BeNull();
    }

    private static WebApplication BuildApp(string? clientSecret, string? clientId = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );

        var settings = new Dictionary<string, string?>
        {
            ["AuthOptions:IssuerUri"] = "https://localhost:3000",
            ["AuthOptions:ClientSecret"] = clientSecret,
        };

        if (clientId is not null)
        {
            settings["AuthOptions:ClientId"] = clientId;
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddDbContext<IdentityContext>(options => options.UseSqlite("DataSource=:memory:"));
        builder.AddCustomIdentityServer();

        return builder.Build();
    }
}
