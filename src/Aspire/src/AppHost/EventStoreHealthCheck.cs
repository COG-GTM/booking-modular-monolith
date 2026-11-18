using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal sealed class EventStoreHealthCheck(EndpointReference endpoint, string caFile) : IHealthCheck
{
    private readonly HttpClient _httpClient = new(
        new SocketsHttpHandler
        {
            SslOptions =
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                {
                    if (certificate is null)
                    {
                        return false;
                    }

                    using var trustedRoot = X509Certificate2.CreateFromPemFile(caFile);
                    using var serverCertificate = certificate as X509Certificate2 ?? new X509Certificate2(certificate);
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(trustedRoot);
                    return chain.Build(serverCertificate);
                },
            },
        }
    )
    {
        Timeout = TimeSpan.FromSeconds(5),
    };

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var endpointUrl = await endpoint.GetValueAsync(cancellationToken);
            using var response = await _httpClient.GetAsync(
                $"{endpointUrl.TrimEnd('/')}/health/live",
                cancellationToken
            );
            return response.IsSuccessStatusCode
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"EventStoreDB returned HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return HealthCheckResult.Unhealthy("EventStoreDB is not reachable over TLS.", exception);
        }
    }
}
