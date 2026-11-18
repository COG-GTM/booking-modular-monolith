extern alias apphost;

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.ApplicationModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Integration.Test.Booking.Infrastructure;

public class EventStoreHealthCheckTests
{
    [Theory]
    [InlineData(204, true, true, HealthStatus.Healthy)]
    [InlineData(503, true, true, HealthStatus.Unhealthy)]
    [InlineData(204, false, true, HealthStatus.Unhealthy)]
    [InlineData(204, true, false, HealthStatus.Unhealthy)]
    public async Task liveness_validates_public_only_ca_http_status_and_server_identity(
        int statusCode,
        bool trustedIssuer,
        bool matchingName,
        HealthStatus expectedStatus
    )
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var root = CreateRootCertificate();
        using var unrelatedRoot = CreateRootCertificate();
        using var serverCertificate = CreateServerCertificate(trustedIssuer ? root : unrelatedRoot, matchingName);
        var directory = Directory.CreateTempSubdirectory();

        try
        {
            var caFile = Path.Combine(directory.FullName, "ca.crt");
            await File.WriteAllTextAsync(caFile, root.ExportCertificatePem(), timeout.Token);
            using var publicRoot = X509Certificate2.CreateFromPem(await File.ReadAllTextAsync(caFile, timeout.Token));
            Assert.False(publicRoot.HasPrivateKey);

            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
                options.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(serverCertificate))
            );
            await using var server = builder.Build();
            var requests = 0;
            server.MapGet(
                "/health/live",
                context =>
                {
                    Interlocked.Increment(ref requests);
                    context.Response.StatusCode = statusCode;
                    return Task.CompletedTask;
                }
            );
            await server.StartAsync(timeout.Token);
            var address = Assert.Single(
                server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses
            );
            var uri = new Uri(address);
            var endpoint = new EndpointAnnotation(System.Net.Sockets.ProtocolType.Tcp, uriScheme: "https");
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, uri.Host, uri.Port);
            var resource = new ContainerResource("eventstore");
            resource.Annotations.Add(endpoint);
            var reference = new EndpointReference(resource, endpoint.Name);
            var checkType = typeof(apphost::Program).Assembly.GetType("EventStoreHealthCheck", throwOnError: true)!;
            var check = Assert.IsAssignableFrom<IHealthCheck>(Activator.CreateInstance(checkType, reference, caFile));

            var result = await check.CheckHealthAsync(new HealthCheckContext(), timeout.Token);

            Assert.Equal(expectedStatus, result.Status);
            if (trustedIssuer && matchingName)
            {
                Assert.Equal(1, Volatile.Read(ref requests));
                Assert.Null(result.Exception);
                if (statusCode == 503)
                {
                    Assert.Equal("EventStoreDB returned HTTP 503.", result.Description);
                }
            }
            else
            {
                Assert.Equal(0, Volatile.Read(ref requests));
                Assert.IsType<HttpRequestException>(result.Exception);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static X509Certificate2 CreateRootCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=EventStore Health Check Test CA",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddDays(-1), now.AddDays(2));
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 root, bool matchingName)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        if (matchingName)
        {
            san.AddIpAddress(IPAddress.Loopback);
            san.AddDnsName("localhost");
        }
        else
        {
            san.AddDnsName("unrelated.invalid");
        }

        request.CertificateExtensions.Add(san.Build());
        var now = DateTimeOffset.UtcNow;
        using var certificate = request.Create(
            root,
            now.AddHours(-1),
            now.AddDays(1),
            RandomNumberGenerator.GetBytes(16)
        );
        return certificate.CopyWithPrivateKey(key);
    }
}
