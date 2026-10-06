using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Gateway.Unit.Test;

public class GatewayJwtOptionsTests
{
    private const string Authority = "https://localhost:3000";
    private const string Audience = "booking-modular-monolith";

    [Fact]
    public void metadata_address_defaults_to_the_authority_discovery_document()
    {
        using var factory = CreateFactory();

        var options = GetJwtBearerOptions(factory);

        Assert.Equal(Authority, options.Authority);
        Assert.Equal(Audience, options.Audience);
        Assert.Equal($"{Authority}/.well-known/openid-configuration", options.MetadataAddress);
    }

    [Fact]
    public void configured_metadata_address_overrides_discovery_but_issuer_is_still_the_authority()
    {
        const string metadataAddress = "http://booking_modular_monolith:80/.well-known/openid-configuration";
        using var factory = CreateFactory(("Jwt:MetadataAddress", metadataAddress));

        var options = GetJwtBearerOptions(factory);

        Assert.Equal(metadataAddress, options.MetadataAddress);
        Assert.Equal(Authority, options.Authority);
        Assert.Equal([Authority], options.TokenValidationParameters.ValidIssuers);
        Assert.Equal([Audience], options.TokenValidationParameters.ValidAudiences);
        Assert.False(options.RequireHttpsMetadata);
    }

    [Fact]
    public void empty_metadata_address_is_ignored()
    {
        using var factory = CreateFactory(("Jwt:MetadataAddress", ""));

        var options = GetJwtBearerOptions(factory);

        Assert.Equal($"{Authority}/.well-known/openid-configuration", options.MetadataAddress);
    }

    private static WebApplicationFactory<Program> CreateFactory(params (string Key, string Value)[] settings)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("test");
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);
        });
    }

    private static JwtBearerOptions GetJwtBearerOptions(WebApplicationFactory<Program> factory)
    {
        return factory
            .Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }
}
