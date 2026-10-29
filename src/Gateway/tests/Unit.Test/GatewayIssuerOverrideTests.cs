using BuildingBlocks.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Xunit;

namespace Gateway.Tests;

public class GatewayIssuerOverrideTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("http://identity-internal/.well-known/openid-configuration")]
    public void CustomAuthorityUsesItsOwnDiscoveryUnlessMetadataIsExplicitlyOverridden(string? metadataAddress)
    {
        const string authority = "https://issuer.example";
        var overrides = new Dictionary<string, string?> { ["Jwt:Authority"] = authority };
        if (metadataAddress is not null)
            overrides["Jwt:MetadataAddress"] = metadataAddress;

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "gateway.json"))
            .AddInMemoryCollection(overrides)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddJwt();
        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var expected = metadataAddress ?? $"{authority}/.well-known/openid-configuration";
        var manager = Assert.IsType<ConfigurationManager<OpenIdConnectConfiguration>>(options.ConfigurationManager);

        Assert.Equal(authority, options.Authority);
        Assert.Contains(authority, options.TokenValidationParameters.ValidIssuers!);
        Assert.Equal(expected, options.MetadataAddress);
        Assert.Equal(expected, manager.MetadataAddress);
    }
}
