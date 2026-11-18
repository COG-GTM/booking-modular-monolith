extern alias apphost;

using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using DotNet.Testcontainers.Builders;
using EventStore.Client;
using Xunit;

namespace Integration.Test.Booking.Infrastructure;

public class EventStoreAppHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task eventstore_uses_an_internal_tls_endpoint_and_secret_credentials(bool publish)
    {
        await using var appHost = await CreateAppHostAsync(publish: publish);
        var eventStore = GetEventStore(appHost);
        var endpoint = Assert.Single(eventStore.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal("https", endpoint.UriScheme);
        Assert.Equal(2113, endpoint.TargetPort);
        Assert.False(endpoint.IsExternal);

        var env = await GetEnvironmentAsync(appHost, eventStore);
        Assert.DoesNotContain("EVENTSTORE_INSECURE", env.Keys);
        var admin = Assert.IsType<ParameterResource>(env["EVENTSTORE_DEFAULT_ADMIN_PASSWORD"]);
        var ops = Assert.IsType<ParameterResource>(env["EVENTSTORE_DEFAULT_OPS_PASSWORD"]);
        Assert.Equal("eventstore-admin-password", admin.Name);
        Assert.Equal("eventstore-ops-password", ops.Name);
        Assert.True(admin.Secret);
        Assert.True(ops.Secret);

        var mount = Assert.Single(
            eventStore.Annotations.OfType<ContainerMountAnnotation>(),
            mount => mount.Target == "/etc/eventstore/certs"
        );
        Assert.True(mount.IsReadOnly);
        Assert.Equal("/etc/eventstore/certs/node/node.crt", env["EVENTSTORE_CERTIFICATE_FILE"]);
        Assert.Equal("/etc/eventstore/certs/node/node.key", env["EVENTSTORE_CERTIFICATE_PRIVATE_KEY_FILE"]);
        Assert.Equal("/etc/eventstore/certs/ca", env["EVENTSTORE_TRUSTED_ROOT_CERTIFICATES_PATH"]);

        var connection = await GetApiConnectionAsync(appHost);
        var caFile = publish ? "/etc/eventstore/certs/ca/ca.crt" : Path.Combine(mount.Source!, "ca", "ca.crt");
        Assert.Equal(
            $"esdb://admin:{{{admin.Name}.value}}@{{eventstore.bindings.https.host}}:{{eventstore.bindings.https.port}}?tls=true&tlsCaFile={caFile}",
            connection.ValueExpression
        );
    }

    [Fact]
    public async Task booking_connection_can_read_eventstore_over_tls_and_anonymous_reads_are_denied()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var root = CreateRootCertificate();
        using var serverCertificate = CreateServerCertificate(root);
        var testDirectory = Directory.CreateTempSubdirectory();

        try
        {
            var appHostDirectory = Directory.CreateDirectory(
                Path.Combine(testDirectory.FullName, "src", "Aspire", "src", "AppHost")
            );
            var caDirectory = Directory.CreateDirectory(
                Path.Combine(testDirectory.FullName, "deployments", "configs", "eventstore", "certs", "ca")
            );
            await File.WriteAllTextAsync(
                Path.Combine(caDirectory.FullName, "ca.crt"),
                root.ExportCertificatePem(),
                timeout.Token
            );

            await using var appHost = await CreateAppHostAsync(
                projectDirectory: appHostDirectory.FullName,
                args:
                [
                    $"--Parameters:eventstore-admin-password={Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}",
                    $"--Parameters:eventstore-ops-password={Convert.ToHexString(RandomNumberGenerator.GetBytes(16))}",
                ]
            );
            var eventStore = GetEventStore(appHost);
            var endpoint = Assert.Single(eventStore.Annotations.OfType<EndpointAnnotation>());
            var port = Assert.IsType<int>(endpoint.TargetPort);
            var image = Assert.Single(eventStore.Annotations.OfType<ContainerImageAnnotation>());
            var env = await GetEnvironmentAsync(appHost, eventStore);
            using var serverKey = serverCertificate.GetRSAPrivateKey()!;
            var containerBuilder = new ContainerBuilder()
                .WithImage($"{image.Image}:{image.Tag}")
                .WithPortBinding(port, true)
                .WithResourceMapping(
                    PemBytes(root.ExportCertificatePem()),
                    (string)env["EVENTSTORE_TRUSTED_ROOT_CERTIFICATES_PATH"] + "/ca.crt"
                )
                .WithResourceMapping(
                    PemBytes(serverCertificate.ExportCertificatePem()),
                    (string)env["EVENTSTORE_CERTIFICATE_FILE"]
                )
                .WithResourceMapping(
                    PemBytes(serverKey.ExportPkcs8PrivateKeyPem()),
                    (string)env["EVENTSTORE_CERTIFICATE_PRIVATE_KEY_FILE"]
                )
                .WithWaitStrategy(
                    Wait.ForUnixContainer().UntilMessageIsLogged("AuthorizationPolicyRegistryFactory' initialized")
                );
            foreach (var (name, value) in env)
            {
                var resolved = value is ParameterResource parameter
                    ? await parameter.GetValueAsync(timeout.Token)
                    : (string)value;
                containerBuilder = containerBuilder.WithEnvironment(name, resolved!);
            }

            await using var container = containerBuilder.Build();
            await container.StartAsync(timeout.Token);
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(
                endpoint,
                container.Hostname,
                container.GetMappedPublicPort(port)
            );
            var expression = await GetApiConnectionAsync(appHost);
            var connectionString = await expression.GetValueAsync(timeout.Token);

            using var client = new EventStoreClient(EventStoreClientSettings.Create(connectionString));
            await using var authenticatedRead = client
                .ReadAllAsync(Direction.Forwards, Position.Start, maxCount: 1, cancellationToken: timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            Assert.True(await authenticatedRead.MoveNextAsync());

            var anonymousConnection = new UriBuilder(connectionString) { UserName = "", Password = "" };
            using var anonymousClient = new EventStoreClient(
                EventStoreClientSettings.Create(anonymousConnection.Uri.AbsoluteUri)
            );
            await using var anonymousRead = anonymousClient
                .ReadAllAsync(Direction.Forwards, Position.Start, maxCount: 1, cancellationToken: timeout.Token)
                .GetAsyncEnumerator(timeout.Token);
            await Assert.ThrowsAsync<AccessDeniedException>(async () =>
            {
                await anonymousRead.MoveNextAsync();
            });
        }
        finally
        {
            testDirectory.Delete(recursive: true);
        }
    }

    private static Task<IDistributedApplicationTestingBuilder> CreateAppHostAsync(
        bool publish = false,
        string? projectDirectory = null,
        string[]? args = null
    ) =>
        DistributedApplicationTestingBuilder.CreateAsync<apphost::Program>(
            publish ? ["--publisher", "docker-compose"] : args ?? [],
            (options, _) =>
            {
                options.DisableDashboard = true;
                options.ProjectDirectory = projectDirectory ?? options.ProjectDirectory;
            }
        );

    private static ContainerResource GetEventStore(IDistributedApplicationTestingBuilder appHost) =>
        Assert.Single(appHost.Resources.OfType<ContainerResource>(), resource => resource.Name == "eventstore");

    private static async Task<ReferenceExpression> GetApiConnectionAsync(IDistributedApplicationTestingBuilder appHost)
    {
        var api = Assert.Single(appHost.Resources.OfType<ProjectResource>(), resource => resource.Name == "api");
        var env = await GetEnvironmentAsync(appHost, api);
        return Assert.IsType<ReferenceExpression>(env["ConnectionStrings__eventstore"]);
    }

    private static byte[] PemBytes(string pem) => System.Text.Encoding.ASCII.GetBytes(pem + "\n");

    private static async Task<Dictionary<string, object>> GetEnvironmentAsync(
        IDistributedApplicationTestingBuilder appHost,
        IResource resource
    )
    {
        Dictionary<string, object> environment = [];
        var context = new EnvironmentCallbackContext(appHost.ExecutionContext, resource, environment);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return environment;
    }

    private static X509Certificate2 CreateRootCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=EventStore Test CA",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        var now = DateTimeOffset.UtcNow;
        return request.CreateSelfSigned(now.AddDays(-1), now.AddDays(2));
    }

    private static X509Certificate2 CreateServerCertificate(X509Certificate2 root)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddIpAddress(IPAddress.Loopback);
        san.AddDnsName("localhost");
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
