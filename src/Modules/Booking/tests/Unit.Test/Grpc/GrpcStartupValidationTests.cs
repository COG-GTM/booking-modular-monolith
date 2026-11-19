using Booking.Configuration;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Unit.Test.Grpc;

public class GrpcStartupValidationTests
{
    [Theory]
    [InlineData("Flight", "Address", "relative", "Address must be an absolute URL")]
    [InlineData("Passenger", "Address", "", "Address must be an absolute URL")]
    [InlineData("Passenger", "Address", "ftp://passenger", "Address must use http or https")]
    [InlineData("Passenger", "Deadline", "-00:00:01", "Deadline must be positive")]
    [InlineData("Passenger", "MaxAttempts", "6", "MaxAttempts must be between 1 and 5")]
    [InlineData("Flight", "InitialBackoff", "00:00:00", "InitialBackoff must be positive")]
    [InlineData("Passenger", "InitialBackoff", "-00:00:01", "InitialBackoff must be positive")]
    [InlineData("Flight", "HealthCheckTimeout", "00:00:00", "HealthCheckTimeout must be positive")]
    [InlineData("Passenger", "HealthCheckTimeout", "-00:00:01", "HealthCheckTimeout must be positive")]
    public void invalid_configuration_is_rejected_at_startup(string client, string option, string value, string error)
    {
        var settings = Settings();
        settings[$"Grpc:{client}:{option}"] = value;
        using var provider = GrpcClientTestHost.Build(settings);

        var start = () => provider.GetRequiredService<IStartupValidator>().Validate();

        start
            .Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should()
            .Contain(failure =>
                failure.Contains($"Grpc:{client}:", StringComparison.Ordinal)
                && failure.Contains(error, StringComparison.Ordinal)
            );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void tls_and_policy_boundaries_are_valid_at_startup(int attempts)
    {
        var settings = Settings();
        settings["Grpc:RequireTls"] = "true";
        foreach (var client in new[] { "Flight", "Passenger" })
        {
            settings[$"Grpc:{client}:MaxAttempts"] = attempts.ToString(
                System.Globalization.CultureInfo.InvariantCulture
            );
            settings[$"Grpc:{client}:InitialBackoff"] = "00:00:01";
            settings[$"Grpc:{client}:MaxBackoff"] = "00:00:01";
        }
        using var provider = GrpcClientTestHost.Build(settings);

        var start = () => provider.GetRequiredService<IStartupValidator>().Validate();

        start.Should().NotThrow();
        var options = provider.GetRequiredService<IOptions<GrpcOptions>>().Value;
        options.Flight.MaxAttempts.Should().Be(attempts);
        options.Passenger.MaxAttempts.Should().Be(attempts);
    }

    private static Dictionary<string, string?> Settings() =>
        new()
        {
            ["Grpc:Flight:Address"] = "https://flight-service:81",
            ["Grpc:Passenger:Address"] = "https://passenger-service:81",
        };
}
