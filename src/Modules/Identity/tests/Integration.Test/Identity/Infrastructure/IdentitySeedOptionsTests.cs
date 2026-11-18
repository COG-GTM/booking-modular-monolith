using System.Collections.Generic;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentitySeedOptionsTests
{
    private static IdentitySeedOptions Resolve(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddIdentityModules();

        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;
    }

    [Fact]
    public void should_bind_seed_passwords_from_the_IdentitySeedOptions_section()
    {
        var options = Resolve(
            new()
            {
                ["IdentitySeedOptions:AdminPassword"] = "admin-from-config",
                ["IdentitySeedOptions:UserPassword"] = "user-from-config",
            }
        );

        options.AdminPassword.Should().Be("admin-from-config");
        options.UserPassword.Should().Be("user-from-config");
    }

    [Fact]
    public void should_bind_seed_passwords_from_double_underscore_environment_variables()
    {
        System.Environment.SetEnvironmentVariable(
            "SEEDOPTIONSTEST_IdentitySeedOptions__AdminPassword",
            "admin-from-env"
        );
        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
            builder.Configuration.Sources.Clear();
            builder.Configuration.AddEnvironmentVariables("SEEDOPTIONSTEST_");
            builder.AddIdentityModules();

            using var provider = builder.Services.BuildServiceProvider();
            var options = provider.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;

            options.AdminPassword.Should().Be("admin-from-env");
            options.UserPassword.Should().BeNull();
        }
        finally
        {
            System.Environment.SetEnvironmentVariable("SEEDOPTIONSTEST_IdentitySeedOptions__AdminPassword", null);
        }
    }

    [Fact]
    public void should_have_no_default_seed_passwords_when_nothing_is_configured()
    {
        var options = Resolve(new());

        options.AdminPassword.Should().BeNull();
        options.UserPassword.Should().BeNull();
    }

    [Fact]
    public void should_not_ship_seed_passwords_in_the_api_appsettings()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(System.AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.docker.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        config.GetSection(nameof(IdentitySeedOptions)).GetChildren().Should().BeEmpty();
    }
}
