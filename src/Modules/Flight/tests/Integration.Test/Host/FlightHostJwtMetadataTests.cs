using Flight.Host.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace Integration.Test.Host;

// AddFlightServiceInfrastructure layers an optional Jwt:MetadataAddress override on top of AddJwt. Outside
// Docker no override is configured, so the handler must still discover metadata from the monolith authority.
// These tests build the host's service collection from its appsettings.json plus in-memory overrides and do
// not start any container.
public class FlightHostJwtMetadataTests
{
    private const string MonolithAuthority = "https://localhost:3000";

    private static JwtBearerOptions BuildJwtBearerOptions(string? metadataAddress)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:MetadataAddress"] = metadataAddress }
        );

        builder.AddFlightServiceInfrastructure();

        using var provider = builder.Services.BuildServiceProvider();

        return provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void should_derive_metadata_from_the_authority_when_no_override_is_configured(string? metadataAddress)
    {
        var options = BuildJwtBearerOptions(metadataAddress);

        options.Authority.Should().Be(MonolithAuthority);
        options.MetadataAddress.Should().Be($"{MonolithAuthority}/.well-known/openid-configuration");
        options
            .ConfigurationManager.Should()
            .BeOfType<ConfigurationManager<OpenIdConnectConfiguration>>()
            .Which.MetadataAddress.Should()
            .Be(options.MetadataAddress);
    }
}
