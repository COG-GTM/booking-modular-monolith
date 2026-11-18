using FluentAssertions;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Integration.Test.Identity.Security;

public class IdentityLockoutOptionsTests
{
    [Fact]
    public void custom_identity_server_configures_new_user_lockout()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["AuthOptions:IssuerUri"] = "https://identity.example.test" }
        );
        builder.AddCustomIdentityServer();
        using var provider = builder.Services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<IdentityOptions>>().Value.Lockout;

        options.AllowedForNewUsers.Should().BeTrue();
        options.MaxFailedAccessAttempts.Should().Be(5);
        options.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(5));
    }
}
