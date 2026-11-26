using System.Collections.Generic;
using FluentAssertions;
using global::Identity.Data.Seed;
using global::Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Seed;

public class IdentitySeedOptionsRegistrationTests
{
    [Fact]
    public void should_bind_configured_seed_passwords_when_registering_identity_module()
    {
        const string adminPassword = "test-admin-seed-value";
        const string userPassword = "test-user-seed-value";

        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AuthOptions:IssuerUri"] = "https://identity.example.test",
                ["IdentitySeedOptions:AdminPassword"] = adminPassword,
                ["IdentitySeedOptions:UserPassword"] = userPassword,
            }
        );

        builder.AddIdentityModules();
        using var provider = builder.Services.BuildServiceProvider();

        var seedOptions = provider.GetRequiredService<IOptions<IdentitySeedOptions>>().Value;

        seedOptions.AdminPassword.Should().Be(adminPassword);
        seedOptions.UserPassword.Should().Be(userPassword);
    }
}
