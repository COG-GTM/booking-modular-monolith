using System;
using System.Collections.Generic;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentitySeedOptionsBindingTests
{
    private const string EnvPrefix = "SEEDBINDINGTEST_";

    private static IdentitySeedOptions ResolveFromIdentityModules(Action<IConfigurationBuilder> configure)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["AuthOptions:ClientSecret"] = "configured-client-secret" }
        );
        configure(builder.Configuration);

        builder.AddIdentityModules();

        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IdentitySeedOptions>();
    }

    [Fact]
    public void should_bind_seed_passwords_from_the_IdentitySeedOptions_section()
    {
        var options = ResolveFromIdentityModules(config =>
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["IdentitySeedOptions:AdminPassword"] = "admin-from-config",
                    ["IdentitySeedOptions:UserPassword"] = "user-from-config",
                }
            )
        );

        options.AdminPassword.Should().Be("admin-from-config");
        options.UserPassword.Should().Be("user-from-config");
    }

    [Fact]
    public void should_bind_the_admin_password_from_the_docker_compose_environment_variable()
    {
        Environment.SetEnvironmentVariable($"{EnvPrefix}IdentitySeedOptions__AdminPassword", "admin-from-env");
        try
        {
            var options = ResolveFromIdentityModules(config => config.AddEnvironmentVariables(EnvPrefix));

            options.AdminPassword.Should().Be("admin-from-env");
            options.UserPassword.Should().BeEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable($"{EnvPrefix}IdentitySeedOptions__AdminPassword", null);
        }
    }

    [Fact]
    public void should_have_no_seed_passwords_when_nothing_is_configured()
    {
        var options = ResolveFromIdentityModules(_ => { });

        options.AdminPassword.Should().BeEmpty();
        options.UserPassword.Should().BeEmpty();
    }

    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.docker.json")]
    [InlineData("appsettings.test.json")]
    public void should_not_ship_credentials_in_non_development_api_appsettings(string file)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(file, optional: false)
            .Build();

        config["AuthOptions:ClientSecret"].Should().BeNull();
        config.GetSection(nameof(IdentitySeedOptions)).GetChildren().Should().BeEmpty();
    }
}
