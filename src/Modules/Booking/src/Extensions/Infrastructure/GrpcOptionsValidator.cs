using Booking.Configuration;
using Microsoft.Extensions.Options;

namespace Booking.Extensions.Infrastructure;

internal sealed class GrpcOptionsValidator : IValidateOptions<GrpcOptions>
{
    public ValidateOptionsResult Validate(string? name, GrpcOptions options)
    {
        var failures = new List<string>();
        Validate("Flight", options.Flight, options.RequireTls, failures);
        Validate("Passenger", options.Passenger, options.RequireTls, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Validate(string client, GrpcEndpointOptions endpoint, bool requireTls, List<string> failures)
    {
        var key = $"{GrpcOptions.SectionName}:{client}";

        if (!Uri.TryCreate(endpoint.Address, UriKind.Absolute, out var uri))
        {
            failures.Add($"{key}:Address must be an absolute URL (e.g. http://_grpc.{client.ToLowerInvariant()}), was '{endpoint.Address}'.");
            return;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            // Grpc.Net.Client has no resolver for Aspire's "https+http://" scheme; use http(s)://_grpc.<service>.
            failures.Add($"{key}:Address must use http or https, was '{uri.Scheme}'.");
        }
        else if (requireTls && uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{key}:Address must use https when {GrpcOptions.SectionName}:RequireTls is true.");
        }

        if (endpoint.Deadline <= TimeSpan.Zero)
            failures.Add($"{key}:Deadline must be positive.");

        if (endpoint.MaxAttempts is < 1 or > 5)
            failures.Add($"{key}:MaxAttempts must be between 1 and 5 (gRPC caps retries at 5 attempts).");

        if (endpoint.InitialBackoff <= TimeSpan.Zero || endpoint.MaxBackoff < endpoint.InitialBackoff)
            failures.Add($"{key}: InitialBackoff must be positive and MaxBackoff >= InitialBackoff.");

        if (endpoint.HealthCheckTimeout <= TimeSpan.Zero)
            failures.Add($"{key}:HealthCheckTimeout must be positive.");
    }
}
