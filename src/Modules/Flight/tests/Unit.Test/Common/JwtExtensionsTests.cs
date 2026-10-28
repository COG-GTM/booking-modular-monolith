using BuildingBlocks.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace Unit.Test.Common;

public class JwtExtensionsTests
{
    private const string Authority = "https://localhost:3000";
    private const string Audience = "booking-modular-monolith";

    [Fact]
    public void AddJwtUsesMetadataOverrideForConfigurationManagerAndKeepsPublicIssuer()
    {
        const string metadataAddress = "http://booking_modular_monolith/.well-known/openid-configuration";
        var options = CreateOptions(metadataAddress);

        Assert.Equal(metadataAddress, options.MetadataAddress);
        var manager = Assert.IsType<ConfigurationManager<OpenIdConnectConfiguration>>(options.ConfigurationManager);
        Assert.Equal(metadataAddress, manager.MetadataAddress);
        Assert.Equal(new[] { Authority }, options.TokenValidationParameters.ValidIssuers);
        Assert.Equal(Audience, options.Audience);
        Assert.False(options.RequireHttpsMetadata);
    }

    [Fact]
    public void AddJwtDerivesMetadataAddressFromAuthorityWhenOverrideIsMissing()
    {
        var options = CreateOptions();
        const string metadataAddress = $"{Authority}/.well-known/openid-configuration";

        Assert.Equal(metadataAddress, options.MetadataAddress);
        var manager = Assert.IsType<ConfigurationManager<OpenIdConnectConfiguration>>(options.ConfigurationManager);
        Assert.Equal(metadataAddress, manager.MetadataAddress);
    }

    private static JwtBearerOptions CreateOptions(string? metadataAddress = null)
    {
        var settings = new Dictionary<string, string?> { ["Jwt:Authority"] = Authority, ["Jwt:Audience"] = Audience };
        if (metadataAddress is not null)
            settings["Jwt:MetadataAddress"] = metadataAddress;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddJwt();
        using var provider = services.BuildServiceProvider();
        return provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }
}
