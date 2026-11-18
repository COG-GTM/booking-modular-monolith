using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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

public class IdentityClientSecretConfigurationTests
{
    private const string ClientId = "client";
    private const string LegacySecret = "secret";

    [Fact]
    public void clients_should_hash_the_supplied_secret_instead_of_the_legacy_literal()
    {
        var client = Config.Clients("from-config").Single();

        client.ClientId.Should().Be(ClientId);
        client.AllowedGrantTypes.Should().BeEquivalentTo(GrantTypes.ResourceOwnerPassword);
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be("from-config".Sha256());
        client.ClientSecrets.Single().Value.Should().NotBe(LegacySecret.Sha256());
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("docker", null)]
    [InlineData("Development", null)]
    [InlineData("Production", "")]
    [InlineData("Production", "   ")]
    public void should_fail_startup_when_client_secret_is_missing_outside_the_test_environment(
        string environment,
        string? clientSecret
    )
    {
        var builder = CreateBuilder(environment, clientSecret);

        var act = () => builder.AddCustomIdentityServer();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("AuthOptions:ClientSecret is not configured*AuthOptions__ClientSecret*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task should_register_the_client_with_the_configured_secret(string environment)
    {
        await using var app = Build(CreateBuilder(environment, "configured-client-secret"));

        var client = await app.Services.GetRequiredService<IClientStore>().FindClientByIdAsync(ClientId);

        client.Should().NotBeNull();
        client!.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be("configured-client-secret".Sha256());
    }

    [Fact]
    public async Task should_use_a_random_per_process_secret_in_the_test_environment_when_none_is_configured()
    {
        await using var first = Build(CreateBuilder("test", clientSecret: null));
        await using var second = Build(CreateBuilder("test", clientSecret: null));

        var firstSecret = await ClientSecretHash(first);
        var secondSecret = await ClientSecretHash(second);

        firstSecret.Should().NotBeNullOrWhiteSpace();
        firstSecret.Should().NotBe(LegacySecret.Sha256());
        firstSecret.Should().NotBe(string.Empty.Sha256());
        secondSecret.Should().NotBe(firstSecret);
    }

    [Fact]
    public async Task should_prefer_a_configured_secret_in_the_test_environment()
    {
        await using var app = Build(CreateBuilder("test", "configured-test-secret"));

        (await ClientSecretHash(app)).Should().Be("configured-test-secret".Sha256());
    }

    [Fact]
    public void identity_modules_should_fail_startup_in_production_without_a_client_secret()
    {
        var builder = CreateBuilder(Environments.Production, clientSecret: null);

        var act = () => builder.AddIdentityModules();

        act.Should().Throw<InvalidOperationException>().WithMessage("AuthOptions:ClientSecret is not configured*");
    }

    private static async Task<string> ClientSecretHash(WebApplication app)
    {
        var client = await app.Services.GetRequiredService<IClientStore>().FindClientByIdAsync(ClientId);
        return client!.ClientSecrets.Single().Value;
    }

    private static WebApplicationBuilder CreateBuilder(string environment, string? clientSecret)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AuthOptions:IssuerUri"] = "https://localhost:3000",
                ["AuthOptions:ClientSecret"] = clientSecret,
            }
        );
        return builder;
    }

    private static WebApplication Build(WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<IdentityContext>(options => options.UseSqlite("DataSource=:memory:"));
        builder.AddCustomIdentityServer();
        return builder.Build();
    }
}
