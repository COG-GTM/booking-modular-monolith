using System;
using System.IO;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityCredentialSettingsTests
{
    [Theory]
    [InlineData("appsettings.json")]
    [InlineData("appsettings.docker.json")]
    [InlineData("appsettings.test.json")]
    public void non_development_settings_should_not_configure_identity_credentials(string settingsFile)
    {
        var settingsPath = Path.Combine(FindRepositoryRoot(), "src", "Api", "src", settingsFile);
        var configuration = new ConfigurationBuilder().AddJsonFile(settingsPath).Build();

        configuration["AuthOptions:ClientSecret"].Should().BeNullOrEmpty();
        configuration["IdentitySeedOptions:AdminPassword"].Should().BeNullOrEmpty();
        configuration["IdentitySeedOptions:UserPassword"].Should().BeNullOrEmpty();
    }

    [Fact]
    public void development_settings_should_not_configure_identity_credentials()
    {
        var apiSettingsPath = Path.Combine(FindRepositoryRoot(), "src", "Api", "src");
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiSettingsPath, "appsettings.json"))
            .AddJsonFile(Path.Combine(apiSettingsPath, "appsettings.Development.json"))
            .Build();

        configuration["AuthOptions:ClientSecret"].Should().BeNullOrEmpty();
        configuration["IdentitySeedOptions:AdminPassword"].Should().BeNullOrEmpty();
        configuration["IdentitySeedOptions:UserPassword"].Should().BeNullOrEmpty();
    }

    private static string FindRepositoryRoot()
    {
        for (
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find booking-modular-monolith.sln.");
    }
}
