using System.Collections.Generic;
using BuildingBlocks.Web;
using FluentAssertions;
using Identity.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentitySeedOptionsTests
{
    private static IdentitySeedOptions Resolve(IDictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddValidateOptions<IdentitySeedOptions>();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IdentitySeedOptions>();
    }

    [Fact]
    public void should_bind_all_fields_from_the_IdentitySeedOptions_section()
    {
        var options = Resolve(
            new Dictionary<string, string?>
            {
                ["IdentitySeedOptions:Enabled"] = "true",
                ["IdentitySeedOptions:AdminUserName"] = "admin",
                ["IdentitySeedOptions:AdminEmail"] = "admin@localhost",
                ["IdentitySeedOptions:AdminFirstName"] = "Local",
                ["IdentitySeedOptions:AdminLastName"] = "Admin",
                ["IdentitySeedOptions:AdminPassPortNumber"] = "00000000",
                ["IdentitySeedOptions:AdminPassword"] = "from-env-or-user-secrets",
            }
        );

        options.Enabled.Should().BeTrue();
        options.AdminUserName.Should().Be("admin");
        options.AdminEmail.Should().Be("admin@localhost");
        options.AdminFirstName.Should().Be("Local");
        options.AdminLastName.Should().Be("Admin");
        options.AdminPassPortNumber.Should().Be("00000000");
        options.AdminPassword.Should().Be("from-env-or-user-secrets");
    }

    [Fact]
    public void should_default_to_disabled_with_no_password_when_section_is_absent()
    {
        var options = Resolve(new Dictionary<string, string?>());

        options.Enabled.Should().BeFalse();
        options.AdminUserName.Should().BeNull();
        options.AdminEmail.Should().BeNull();
        options.AdminPassword.Should().BeNull();
    }

    [Fact]
    public void should_leave_password_unset_when_only_non_secret_fields_are_configured()
    {
        var options = Resolve(
            new Dictionary<string, string?>
            {
                ["IdentitySeedOptions:Enabled"] = "true",
                ["IdentitySeedOptions:AdminUserName"] = "admin",
                ["IdentitySeedOptions:AdminEmail"] = "admin@localhost",
            }
        );

        options.Enabled.Should().BeTrue();
        options.AdminPassword.Should().BeNull();
    }
}
