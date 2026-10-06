using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Jwt;

public class JwtExtensionsTests
{
    private const string Authority = "https://localhost:3000";
    private const string Audience = "booking-modular-monolith";
    private const string InternalMetadataAddress =
        "http://booking_modular_monolith:80/.well-known/openid-configuration";

    [Fact]
    public void metadata_address_defaults_to_the_authority_discovery_document()
    {
        var options = BuildJwtBearerOptions();

        options.Authority.Should().Be(Authority);
        options.Audience.Should().Be(Audience);
        options.MetadataAddress.Should().Be($"{Authority}/.well-known/openid-configuration");
    }

    [Fact]
    public void configured_metadata_address_is_applied_to_the_bearer_scheme_options()
    {
        var options = BuildJwtBearerOptions(InternalMetadataAddress);

        options.MetadataAddress.Should().Be(InternalMetadataAddress);
        options.Authority.Should().Be(Authority);
        options.TokenValidationParameters.ValidIssuers.Should().Equal(Authority);
        options.TokenValidationParameters.ValidAudiences.Should().Equal(Audience);
    }

    [Fact]
    public void configured_metadata_address_is_used_by_the_oidc_configuration_manager()
    {
        var options = BuildJwtBearerOptions(InternalMetadataAddress);

        options
            .ConfigurationManager.Should()
            .BeOfType<Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>>()
            .Which.MetadataAddress.Should()
            .Be(InternalMetadataAddress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void blank_metadata_address_falls_back_to_the_authority_discovery_document(string metadataAddress)
    {
        var options = BuildJwtBearerOptions(metadataAddress);

        options.MetadataAddress.Should().Be($"{Authority}/.well-known/openid-configuration");
    }

    [Fact]
    public void metadata_override_is_scoped_to_the_bearer_scheme_and_leaves_unnamed_options_alone()
    {
        var provider = BuildServiceProvider(InternalMetadataAddress);

        var unnamed = provider.GetRequiredService<IOptions<JwtBearerOptions>>().Value;

        unnamed.MetadataAddress.Should().BeNullOrEmpty();
        unnamed.Authority.Should().BeNull();
    }

    private static JwtBearerOptions BuildJwtBearerOptions(string? metadataAddress = null)
    {
        return BuildServiceProvider(metadataAddress)
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static ServiceProvider BuildServiceProvider(string? metadataAddress)
    {
        var settings = new Dictionary<string, string?> { ["Jwt:Authority"] = Authority, ["Jwt:Audience"] = Audience };
        if (metadataAddress is not null)
        {
            settings["Jwt:MetadataAddress"] = metadataAddress;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddJwt();

        return services.BuildServiceProvider();
    }
}
