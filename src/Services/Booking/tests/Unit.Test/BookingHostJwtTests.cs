using Booking.Host.Extensions;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Booking.Host.Unit.Test;

public class BookingHostJwtTests
{
    private static JwtBearerOptions BuildJwtOptions(params KeyValuePair<string, string?>[] overrides)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                ApplicationName = typeof(Program).Assembly.GetName().Name,
                ContentRootPath = AppContext.BaseDirectory,
                EnvironmentName = "test",
            }
        );
        builder.Configuration.AddInMemoryCollection(overrides);
        builder.AddBookingHost();

        using var app = builder.Build();

        return app
            .Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    [Fact]
    public void validates_tokens_issued_by_the_monolith_identity_authority()
    {
        var options = BuildJwtOptions();

        options.Authority.Should().Be("https://localhost:3000");
        options.Audience.Should().Be("booking-modular-monolith");
        options.TokenValidationParameters.ValidIssuers.Should().ContainSingle().Which.Should().Be(options.Authority);
        options.TokenValidationParameters.ValidAudiences.Should().ContainSingle().Which.Should().Be(options.Audience);
    }

    [Fact]
    public void discovers_metadata_from_authority_when_no_metadata_address_is_configured()
    {
        var options = BuildJwtOptions(new KeyValuePair<string, string?>("Jwt:MetadataAddress", null));

        options.MetadataAddress.Should().Be("https://localhost:3000/.well-known/openid-configuration");
    }

    [Fact]
    public void splits_issuer_from_metadata_address_for_in_network_discovery()
    {
        const string metadataAddress = "http://booking_modular_monolith/.well-known/openid-configuration";

        var options = BuildJwtOptions(new KeyValuePair<string, string?>("Jwt:MetadataAddress", metadataAddress));

        options.MetadataAddress.Should().Be(metadataAddress);
        options.Authority.Should().Be("https://localhost:3000");
        options
            .TokenValidationParameters.ValidIssuers.Should()
            .ContainSingle()
            .Which.Should()
            .Be("https://localhost:3000");
        options.RequireHttpsMetadata.Should().BeFalse();
    }
}
