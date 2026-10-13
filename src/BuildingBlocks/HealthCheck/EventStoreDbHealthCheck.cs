using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.HealthCheck;

// EventStoreDB/KurrentDB gRPC clients use `esdb://host:port[,host:port]?tls=...` connection strings, which the
// TCP-based AspNetCore.HealthChecks.EventStore check cannot parse; the node's own HTTP liveness endpoint is
// served on the same port.
public sealed class EventStoreDbHealthCheck(Func<string?> connectionString, HttpClient? httpClient = null) : IHealthCheck
{
    private static readonly HttpClient SharedClient = new();

    private readonly HttpClient _httpClient = httpClient ?? SharedClient;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (!TryGetLivenessUri(connectionString(), out var uri))
            return new HealthCheckResult(context.Registration.FailureStatus, "Invalid EventStoreDB connection string.");

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);

            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : new HealthCheckResult(
                    context.Registration.FailureStatus,
                    $"EventStoreDB liveness returned {(int)response.StatusCode}."
                );
        }
        catch (System.Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, exception: ex);
        }
    }

    public static bool TryGetLivenessUri(string? connectionString, out Uri uri)
    {
        uri = null!;

        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        var schemeEnd = connectionString.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return false;

        var rest = connectionString[(schemeEnd + 3)..];
        var queryStart = rest.IndexOfAny(['?', '/']);
        var hosts = queryStart < 0 ? rest : rest[..queryStart];
        var query = queryStart < 0 ? string.Empty : rest[queryStart..];

        var firstHost = hosts.Split(',', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstHost is null)
            return false;

        var atIndex = firstHost.LastIndexOf('@');
        if (atIndex >= 0)
            firstHost = firstHost[(atIndex + 1)..];

        var tls = !query.Contains("tls=false", StringComparison.OrdinalIgnoreCase);

        return Uri.TryCreate($"{(tls ? "https" : "http")}://{firstHost}/health/live", UriKind.Absolute, out uri!);
    }
}
