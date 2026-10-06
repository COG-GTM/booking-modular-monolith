using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Passenger.Host.Extensions;
using Xunit;

namespace Host.Test.Composition;

// JwtBearerOptions.MetadataAddress is only a seed value: the handler fetches the discovery document and
// signing keys through options.ConfigurationManager, which JwtBearerPostConfigureOptions builds once.
// These tests check the override reaches the ConfigurationManager, not just the options property.
public class JwtMetadataAddressOverrideTests
{
    private const string Authority = "https://localhost:3000";
    private const string DockerMetadataAddress = "http://booking_modular_monolith/.well-known/openid-configuration";

    private static WebApplication BuildHost(params KeyValuePair<string, string?>[] overrides)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ApplicationName = "Passenger.Host" }
        );

        builder.Configuration.AddJsonFile("passenger-host-appsettings.json");
        builder.Configuration.AddInMemoryCollection(overrides);

        builder.AddPassengerHost();

        return builder.Build();
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> GetOidcConfigurationManager(WebApplication app)
    {
        var options = app
            .Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        return options
            .ConfigurationManager.Should()
            .BeOfType<ConfigurationManager<OpenIdConnectConfiguration>>()
            .Subject;
    }

    [Fact]
    public async Task oidc_discovery_should_use_the_authority_when_no_override_is_configured()
    {
        await using var app = BuildHost();

        GetOidcConfigurationManager(app).MetadataAddress.Should().Be($"{Authority}/.well-known/openid-configuration");
    }

    [Fact]
    public async Task oidc_discovery_should_use_the_metadata_address_override()
    {
        await using var app = BuildHost(
            new KeyValuePair<string, string?>("Jwt:MetadataAddress", DockerMetadataAddress)
        );

        GetOidcConfigurationManager(app).MetadataAddress.Should().Be(DockerMetadataAddress);
    }
}
