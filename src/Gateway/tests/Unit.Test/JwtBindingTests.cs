using BuildingBlocks.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gateway.Tests;

public class JwtBindingTests
{
    [Theory]
    [InlineData("http://identity-internal/.well-known/openid-configuration")]
    [InlineData(null)]
    public void AddJwtBindsMetadataWithoutGatewayConfiguration(string? metadata)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Authority"] = "https://issuer.example",
                    ["Jwt:Audience"] = "booking",
                    ["Jwt:MetadataAddress"] = metadata,
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddJwt();
        using var provider = services.BuildServiceProvider();
        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        Assert.Equal(metadata ?? "https://issuer.example/.well-known/openid-configuration", options.MetadataAddress);
        Assert.Equal("https://issuer.example", options.Authority);
        Assert.Equal("booking", options.Audience);
        Assert.False(options.MapInboundClaims);
    }
}
