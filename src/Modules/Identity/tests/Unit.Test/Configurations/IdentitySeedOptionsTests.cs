using BuildingBlocks.Web;
using FluentAssertions;
using Identity.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Configurations;

public class IdentitySeedOptionsTests
{
    [Fact]
    public void should_leave_passwords_null_when_section_is_absent()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = Resolve(configuration);

        options.AdminPassword.Should().BeNull();
        options.UserPassword.Should().BeNull();
    }

    private static IdentitySeedOptions Resolve(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddValidateOptions<IdentitySeedOptions>();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IdentitySeedOptions>();
    }
}
