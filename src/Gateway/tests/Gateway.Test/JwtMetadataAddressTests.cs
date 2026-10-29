using BuildingBlocks.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gateway.Test;

public class JwtMetadataAddressTests
{
    [Theory]
    [InlineData("http://internal.test/.well-known/openid-configuration")]
    [InlineData(null)]
    [InlineData("")]
    public void metadata_address_uses_override_or_authority_discovery(string? metadataAddress)
    {
        const string authority = "http://public.test";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Authority"] = authority,
                    ["Jwt:Audience"] = "booking",
                    ["Jwt:MetadataAddress"] = metadataAddress,
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

        Assert.Equal(
            string.IsNullOrEmpty(metadataAddress) ? authority + "/.well-known/openid-configuration" : metadataAddress,
            options.MetadataAddress
        );
        Assert.Equal(authority, options.Authority);
        Assert.Equal([authority], options.TokenValidationParameters.ValidIssuers);
        Assert.Equal(["booking"], options.TokenValidationParameters.ValidAudiences);
        Assert.False(options.MapInboundClaims);
    }
}
