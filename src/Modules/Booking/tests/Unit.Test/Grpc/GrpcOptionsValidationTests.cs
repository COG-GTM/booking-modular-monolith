using Booking.Configuration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcOptionsValidationTests
{
    private static OptionsValidationException? Validate(Dictionary<string, string?> settings)
    {
        using var sp = GrpcClientTestHost.Build(settings);
        try
        {
            _ = sp.GetRequiredService<IOptions<GrpcOptions>>().Value;
            return null;
        }
        catch (OptionsValidationException ex)
        {
            return ex;
        }
    }

    private static Dictionary<string, string?> Valid() => new()
    {
        ["Grpc:Flight:Address"] = "http://_grpc.flight",
        ["Grpc:Passenger:Address"] = "http://passenger-service:81",
    };

    [Fact]
    public void logical_and_dns_addresses_are_accepted()
    {
        Validate(Valid()).Should().BeNull();
    }

    [Fact]
    public void missing_address_fails_fast()
    {
        var settings = Valid();
        settings.Remove("Grpc:Passenger:Address");

        Validate(settings)!.Message.Should().Contain("Grpc:Passenger:Address");
    }

    [Fact]
    public void aspire_scheme_selection_syntax_is_rejected()
    {
        var settings = Valid();
        settings["Grpc:Flight:Address"] = "https+http://flight";

        Validate(settings)!.Message.Should().Contain("Grpc:Flight:Address must use http or https");
    }

    [Fact]
    public void require_tls_rejects_plain_text_addresses()
    {
        var settings = Valid();
        settings["Grpc:RequireTls"] = "true";
        settings["Grpc:Flight:Address"] = "https://flight-service";

        var error = Validate(settings);

        error!.Message.Should().Contain("Grpc:Passenger:Address must use https");
        error.Message.Should().NotContain("Grpc:Flight:Address");
    }

    [Theory]
    [InlineData("Grpc:Flight:MaxAttempts", "0")]
    [InlineData("Grpc:Flight:MaxAttempts", "6")]
    [InlineData("Grpc:Flight:Deadline", "00:00:00")]
    [InlineData("Grpc:Flight:MaxBackoff", "00:00:00.001")]
    public void out_of_range_policies_are_rejected(string key, string value)
    {
        var settings = Valid();
        settings[key] = value;

        Validate(settings).Should().NotBeNull();
    }
}
