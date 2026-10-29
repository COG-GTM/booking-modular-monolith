using System.Collections.Generic;
using System.Threading.Tasks;
using BuildingBlocks.Web;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Configurations;
using Identity.Data;
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
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task should_register_no_client_when_secret_is_missing(string? clientSecret)
    {
        await using var app = BuildApp(clientSecret);

        var clientStore = app.Services.GetRequiredService<IClientStore>();

        (await clientStore.FindClientByIdAsync("client")).Should().BeNull();
    }

    [Fact]
    public async Task should_register_client_from_configured_secret_and_bind_auth_options()
    {
        const string clientSecret = "s3cret-from-config";
        await using var app = BuildApp(clientSecret);

        var clientStore = app.Services.GetRequiredService<IClientStore>();
        var client = await clientStore.FindClientByIdAsync("client");

        client.Should().NotBeNull();
        client!.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(clientSecret.Sha256());
        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.ResourceOwnerPassword);
        app.Services.GetRequiredService<AuthOptions>().ClientSecret.Should().Be(clientSecret);
    }

    private static WebApplication BuildApp(string? clientSecret)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = Environments.Development }
        );

        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AuthOptions:IssuerUri"] = "https://localhost:3000",
                ["AuthOptions:ClientSecret"] = clientSecret,
            }
        );
        builder.Services.AddDbContext<IdentityContext>(options => options.UseSqlite("DataSource=:memory:"));
        builder.AddCustomIdentityServer();

        return builder.Build();
    }
}
