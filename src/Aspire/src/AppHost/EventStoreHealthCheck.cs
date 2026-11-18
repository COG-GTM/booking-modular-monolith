using System.IO;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

internal sealed class EventStoreHealthCheck : IHealthCheck
{
    private readonly EndpointReference _endpoint;
    private readonly X509Certificate2 _trustedRoot;
    private readonly HttpClient _httpClient;

    public EventStoreHealthCheck(EndpointReference endpoint, string caFile)
    {
        _endpoint = endpoint;
        _trustedRoot = X509Certificate2.CreateFromPem(File.ReadAllText(caFile));
        _httpClient = new HttpClient(
            new SocketsHttpHandler
            {
                SslOptions =
                {
                    RemoteCertificateValidationCallback = (_, certificate, _, sslPolicyErrors) =>
                    {
                        if (
                            certificate is null
                            || (sslPolicyErrors & ~SslPolicyErrors.RemoteCertificateChainErrors) != SslPolicyErrors.None
                        )
                        {
                            return false;
                        }

                        using var serverCertificate =
                            certificate as X509Certificate2 ?? new X509Certificate2(certificate);
                        using var chain = new X509Chain();
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain.ChainPolicy.CustomTrustStore.Add(_trustedRoot);
                        return chain.Build(serverCertificate);
                    },
                },
            }
        )
        {
            Timeout = TimeSpan.FromSeconds(5),
        };
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var endpointUrl = await _endpoint.GetValueAsync(cancellationToken);
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
