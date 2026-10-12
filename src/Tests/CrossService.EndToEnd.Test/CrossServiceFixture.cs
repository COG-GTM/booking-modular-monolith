namespace CrossService.EndToEnd.Test;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using BuildingBlocks.TestBase;
using DotNet.Testcontainers.Containers;
using Testcontainers.EventStoreDb;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CrossServiceCollection : ICollectionFixture<CrossServiceFixture>
{
    public const string Name = "Cross service E2E";
}

public sealed class CrossServiceFixture : IAsyncLifetime
{
    private readonly List<IContainer> containers = [];
    private readonly List<HostProcess> hosts = [];
    private string outputDirectory = string.Empty;

    public int IdentityPort { get; private set; }
    public int FlightPort { get; private set; }
    public int FlightGrpcPort { get; private set; }
    public int PassengerPort { get; private set; }
    public int PassengerGrpcPort { get; private set; }
    public int BookingPort { get; private set; }
    public int BookingGrpcPort { get; private set; }
    public int GatewayPort { get; private set; }
    public string PassengerDatabaseConnectionString { get; private set; } = string.Empty;
    public RabbitMqContainer RabbitMq { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        outputDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "e2e-logs");
        Directory.CreateDirectory(outputDirectory);
        try
        {
            await StartContainersAsync();

            IdentityPort = FreePort();
            FlightPort = FreePort();
            FlightGrpcPort = FreePort();
            PassengerPort = FreePort();
            PassengerGrpcPort = FreePort();
            BookingPort = FreePort();
            BookingGrpcPort = FreePort();
            GatewayPort = FreePort();

            var repoRoot = FindRepositoryRoot();
            var configuration =
                Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                ?? "Debug";
            var sslCertDir =
                $"{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}/.aspnet/dev-certs/trust:/usr/lib/ssl/certs";
            var identityUri = $"http://localhost:{IdentityPort}";

            await StartHostAsync(
                "identity",
                Path.Combine(repoRoot, "src/Services/Identity/src/bin", configuration, "net10.0", "Identity.Host.dll"),
                IdentityPort,
                CommonEnvironment(IdentityPort, sslCertDir)
                    .Concat(
                        [
                            Pair("AuthOptions__IssuerUri", identityUri),
                            Pair("PostgresOptions__ConnectionString__Identity", PostgresConnection("identity_service")),
                        ]
                    )
            );

            await StartHostAsync(
                "flight",
                Path.Combine(
                    repoRoot,
                    "src/Services/Flight/Flight.Host/bin",
                    configuration,
                    "net10.0",
                    "Flight.Host.dll"
                ),
                FlightPort,
                ServiceEnvironment(FlightPort, FlightGrpcPort, IdentityPort, sslCertDir)
                    .Concat(
                        [
                            Pair("PostgresOptions__ConnectionString__Flight", PostgresConnection("flight_service")),
                            Pair("MongoOptions__ConnectionString", MongoConnection()),
                            Pair("MongoOptions__Flight__DatabaseName", "flight_service_read"),
                        ]
                    )
            );

            await StartHostAsync(
                "passenger",
                Path.Combine(
                    repoRoot,
                    "src/Services/Passenger.Host/bin",
                    configuration,
                    "net10.0",
                    "Passenger.Host.dll"
                ),
                PassengerPort,
                ServiceEnvironment(PassengerPort, PassengerGrpcPort, IdentityPort, sslCertDir)
                    .Concat(
                        [
                            Pair("PostgresOptions__ConnectionString__Passenger", PassengerDatabaseConnectionString),
                            Pair("MongoOptions__ConnectionString", MongoConnection()),
                            Pair("MongoOptions__Passenger__DatabaseName", "passenger_service_read"),
                        ]
                    )
            );

            await StartHostAsync(
                "booking",
                Path.Combine(repoRoot, "src/Services/Booking/src/bin", configuration, "net10.0", "Booking.Host.dll"),
                BookingPort,
                ServiceEnvironment(BookingPort, BookingGrpcPort, IdentityPort, sslCertDir)
                    .Concat(
                        [
                            Pair("PostgresOptions__ConnectionString__Booking", PostgresConnection("booking_service")),
                            Pair("MongoOptions__ConnectionString", MongoConnection()),
                            Pair("MongoOptions__Booking__DatabaseName", "booking_service_read"),
                            Pair("EventStoreOptions__ConnectionString", EventStore.GetConnectionString()),
                            Pair("Grpc__Flight__Address", "http://flight"),
                            Pair("Services__flight__http__0", $"http://localhost:{FlightGrpcPort}"),
                            Pair("Grpc__Passenger__Address", "http://passenger"),
                            Pair("Services__passenger__http__0", $"http://localhost:{PassengerGrpcPort}"),
                        ]
                    )
            );

            var gatewayEnvironment = CommonEnvironment(GatewayPort, sslCertDir)
                .Concat(
                    [
                        Pair("Jwt__Authority", identityUri),
                        Pair("ReverseProxy__Clusters__identity__Destinations__monolith__Address", identityUri),
                        Pair(
                            "ReverseProxy__Clusters__flight__Destinations__monolith__Address",
                            $"http://localhost:{FlightPort}"
                        ),
                        Pair(
                            "ReverseProxy__Clusters__passenger__Destinations__monolith__Address",
                            $"http://localhost:{PassengerPort}"
                        ),
                        Pair(
                            "ReverseProxy__Clusters__booking__Destinations__monolith__Address",
                            $"http://localhost:{BookingPort}"
                        ),
                        // No monolith host runs in this topology, so its fallback cluster points at Identity.
                        Pair("ReverseProxy__Clusters__monolith__Destinations__monolith__Address", identityUri),
                    ]
                );
            await StartHostAsync(
                "gateway",
                Path.Combine(repoRoot, "src/Gateway/src/bin", configuration, "net10.0", "Gateway.dll"),
                GatewayPort,
                gatewayEnvironment
            );
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        var runningHosts = hosts.AsEnumerable().Reverse().ToArray();
        hosts.Clear();
        var runningContainers = containers.AsEnumerable().Reverse().ToArray();
        containers.Clear();
        var errors = new List<Exception>();
        foreach (var host in runningHosts)
        {
            try
            {
                await host.StopAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        foreach (var container in runningContainers)
        {
            try
            {
                await container.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException("Failed to dispose cross-service test resources.", errors);
        }
    }

    private PostgreSqlContainer Postgres { get; set; } = null!;
    private MongoDbContainer Mongo { get; set; } = null!;
    private EventStoreDbContainer EventStore { get; set; } = null!;

    private async Task StartContainersAsync()
    {
        Postgres = TestContainers.PostgresTestContainer();
        Mongo = TestContainers.MongoTestContainer();
        RabbitMq = TestContainers.RabbitMqTestContainer();
        EventStore = TestContainers.EventStoreTestContainer();

        containers.AddRange([Postgres, Mongo, RabbitMq, EventStore]);
        foreach (var container in containers)
        {
            await container.StartAsync();
        }

        var passengerBuilder = new Npgsql.NpgsqlConnectionStringBuilder(Postgres.GetConnectionString())
        {
            Database = "passenger_service",
        };
        PassengerDatabaseConnectionString = passengerBuilder.ConnectionString;
    }

    private IEnumerable<KeyValuePair<string, string?>> CommonEnvironment(int httpPort, string sslCertDir)
    {
        yield return Pair("ASPNETCORE_ENVIRONMENT", "Development");
        yield return Pair("ASPNETCORE_URLS", $"http://localhost:{httpPort}");
        yield return Pair("Kestrel__Endpoints__Http__Url", $"http://localhost:{httpPort}");
        yield return Pair("Jwt__Authority", $"http://localhost:{IdentityPort}");
        yield return Pair("RabbitMqOptions__HostName", RabbitMq.Hostname);
        yield return Pair("RabbitMqOptions__Port", RabbitMq.GetMappedPublicPort(5672).ToString());
        yield return Pair("RabbitMqOptions__UserName", TestContainers.RabbitMqContainerConfiguration.UserName);
        yield return Pair("RabbitMqOptions__Password", TestContainers.RabbitMqContainerConfiguration.Password);
        yield return Pair("PersistMessageOptions__Interval", "1");
        yield return Pair("ObservabilityOptions__UseOTLPExporter", "false");
        yield return Pair("ObservabilityOptions__UseAspireOTLPExporter", "false");
        yield return Pair("SSL_CERT_DIR", sslCertDir);
    }

    private IEnumerable<KeyValuePair<string, string?>> ServiceEnvironment(
        int httpPort,
        int grpcPort,
        int identityPort,
        string sslCertDir
    )
    {
        foreach (var item in CommonEnvironment(httpPort, sslCertDir))
        {
            yield return item;
        }

        yield return Pair("Jwt__Authority", $"http://localhost:{identityPort}");
        yield return Pair("Kestrel__Endpoints__Http__Protocols", "Http1");
        yield return Pair("Kestrel__Endpoints__Grpc__Url", $"http://localhost:{grpcPort}");
        yield return Pair("Kestrel__Endpoints__Grpc__Protocols", "Http2");
    }

    private async Task StartHostAsync(
        string service,
        string dllPath,
        int httpPort,
        IEnumerable<KeyValuePair<string, string?>> environment
    )
    {
        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"Built host assembly not found: {dllPath}", dllPath);
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(dllPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(dllPath);
        foreach (var (key, value) in environment)
        {
            startInfo.Environment[key] = value;
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var log = new StreamWriter(Path.Combine(outputDirectory, $"{service}.log"), append: false) { AutoFlush = true };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                log.WriteLine(args.Data);
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
                log.WriteLine(args.Data);
        };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        hosts.Add(new HostProcess(process, log));

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var healthUrl = $"http://localhost:{httpPort}/health";
        var lastHealthResponse = "No response received.";
        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException(
                    $"{service} exited before readiness. Last /health response: {lastHealthResponse}{Environment.NewLine}{TailLog(service)}"
                );
            }

            try
            {
                using var healthResponse = await client.GetAsync(healthUrl);
                if (healthResponse.IsSuccessStatusCode)
                {
                    return;
                }

                var healthBody = await healthResponse.Content.ReadAsStringAsync();
                lastHealthResponse = $"HTTP {(int)healthResponse.StatusCode} {healthResponse.StatusCode}: {healthBody}";
            }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException(
            $"Timed out waiting for {service} at {healthUrl}. Last /health response: {lastHealthResponse}{Environment.NewLine}{TailLog(service)}"
        );
    }

    private string TailLog(string service)
    {
        var path = Path.Combine(outputDirectory, $"{service}.log");
        return File.Exists(path)
            ? string.Join(Environment.NewLine, File.ReadAllLines(path).TakeLast(60))
            : "No host log was created.";
    }

    private string PostgresConnection(string database)
    {
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(Postgres.GetConnectionString()) { Database = database };
        return builder.ConnectionString;
    }

    private string MongoConnection() => Mongo.GetConnectionString();

    private string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "booking-modular-monolith.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate booking-modular-monolith.sln.");
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static KeyValuePair<string, string?> Pair(string key, string value) => new(key, value);

    private sealed class HostProcess(Process process, StreamWriter log)
    {
        public async Task StopAsync()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }

            process.Dispose();
            await log.DisposeAsync();
        }
    }
}
