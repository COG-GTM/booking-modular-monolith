namespace Microservices.EndToEnd.Test;

using DotNet.Testcontainers.Containers;
using Npgsql;
using Testcontainers.EventStoreDb;
using Testcontainers.MongoDb;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MicroservicesCollection : ICollectionFixture<MicroservicesFixture>
{
    public const string Name = "Microservices E2E";
}

// Real infrastructure in Testcontainers + Identity, Flight, Passenger, Booking hosts and the gateway as
// separate processes on free ports, each on its own service-owned databases and RabbitMQ transport.
public sealed class MicroservicesFixture : IAsyncLifetime
{
    public const string RabbitUser = "e2e";
    public const string RabbitPassword = "e2e-password";

    private readonly List<IContainer> containers = [];
    private readonly List<HostProcess> hosts = [];

    public Uri GatewayAddress { get; private set; } = null!;

    public string PostgresConnectionString { get; private set; } = string.Empty;

    public string EventStoreConnectionString { get; private set; } = string.Empty;

    public Uri RabbitManagementAddress { get; private set; } = null!;

    public string LogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "e2e-logs");

    public string Logs => string.Join("\n\n", hosts.Select(host => $"--- {host.Name} ---\n{host.Tail(40)}"));

    public string ConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder(PostgresConnectionString) { Database = database }.ConnectionString;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(LogDirectory);
        try
        {
            await StartInfrastructureAsync();
            await StartHostsAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var host in Enumerable.Reverse(hosts))
        {
            await host.DisposeAsync();
        }

        hosts.Clear();
        foreach (var container in containers)
        {
            await container.DisposeAsync();
        }

        containers.Clear();
    }

    private async Task StartInfrastructureAsync()
    {
        var postgres = new PostgreSqlBuilder()
            .WithImage(Image("E2E_POSTGRES_IMAGE", "postgres:16"))
            .WithCommand("-c", "max_prepared_transactions=100")
            .Build();
        var mongo = new MongoDbBuilder().WithImage(Image("E2E_MONGO_IMAGE", "mongo:latest")).Build();
        var rabbit = new RabbitMqBuilder()
            .WithImage(Image("E2E_RABBITMQ_IMAGE", "rabbitmq:3-management"))
            .WithUsername(RabbitUser)
            .WithPassword(RabbitPassword)
            .WithPortBinding(15672, true)
            .Build();
        var eventStore = new EventStoreDbBuilder()
            .WithImage(Image("E2E_EVENTSTORE_IMAGE", "eventstore/eventstore:latest"))
            .Build();

        containers.AddRange([postgres, mongo, rabbit, eventStore]);
        await Task.WhenAll(containers.Select(container => container.StartAsync()));

        PostgresConnectionString = postgres.GetConnectionString();
        EventStoreConnectionString = eventStore.GetConnectionString();
        MongoConnectionString = mongo.GetConnectionString();
        RabbitHost = rabbit.Hostname;
        RabbitPort = rabbit.GetMappedPublicPort(5672);
        RabbitManagementAddress = new Uri($"http://{rabbit.Hostname}:{rabbit.GetMappedPublicPort(15672)}");
    }

    private string MongoConnectionString { get; set; } = string.Empty;

    private string RabbitHost { get; set; } = string.Empty;

    private int RabbitPort { get; set; }

    private async Task StartHostsAsync()
    {
        var identity = Http(HostProcess.FreePort());
        var flight = Http(HostProcess.FreePort());
        var flightGrpc = Http(HostProcess.FreePort());
        var passenger = Http(HostProcess.FreePort());
        var passengerGrpc = Http(HostProcess.FreePort());
        var booking = Http(HostProcess.FreePort());
        GatewayAddress = Http(HostProcess.FreePort());

        await StartAsync(
            "identity",
            "src/Services/Identity.Host/src",
            "Identity.Host",
            identity,
            Service("identity", identity, identity, null)
                .With("AuthOptions__IssuerUri", Url(identity))
                .With("PostgresOptions__ConnectionString__Identity", ConnectionString("identity_service"))
                .With(
                    "PersistMessageOptions__ConnectionString__Identity",
                    ConnectionString("identity_service_persist_message")
                )
        );

        // Flight, Passenger and Booking start in parallel; they only depend on Identity (JWT metadata).
        await Task.WhenAll(
            StartAsync(
                "flight",
                "src/Services/Flight.Host/src",
                "Flight.Host",
                flight,
                Service("flight", flight, identity, flightGrpc)
                    .With("PostgresOptions__ConnectionString__Flight", ConnectionString("flight_service"))
                    .With(
                        "PersistMessageOptions__ConnectionString__Flight",
                        ConnectionString("flight_service_persist_message")
                    )
                    .With("MongoOptions__Flight__DatabaseName", "flight_service_read")
            ),
            StartAsync(
                "passenger",
                "src/Services/Passenger.Host/src",
                "Passenger.Host",
                passenger,
                Service("passenger", passenger, identity, passengerGrpc)
                    .With("PostgresOptions__ConnectionString__Passenger", ConnectionString("passenger_service"))
                    .With(
                        "PersistMessageOptions__ConnectionString__Passenger",
                        ConnectionString("passenger_service_persist_message")
                    )
                    .With("MongoOptions__Passenger__DatabaseName", "passenger_service_read")
            ),
            StartAsync(
                "booking",
                "src/Services/Booking.Host/src",
                "Booking.Host",
                booking,
                Service("booking", booking, identity, null)
                    .With(
                        "PersistMessageOptions__ConnectionString__Booking",
                        ConnectionString("booking_service_persist_message")
                    )
                    .With("MongoOptions__Booking__DatabaseName", "booking_service_read")
                    .With("EventStoreOptions__ConnectionString", EventStoreConnectionString)
                    .With("Grpc__FlightAddress", Url(flightGrpc))
                    .With("Grpc__PassengerAddress", Url(passengerGrpc))
            )
        );

        var gateway = Common(GatewayAddress, identity)
            .With("ReverseProxy__Clusters__identity__Destinations__monolith__Address", Url(identity))
            .With("ReverseProxy__Clusters__flight__Destinations__monolith__Address", Url(flight))
            .With("ReverseProxy__Clusters__passenger__Destinations__monolith__Address", Url(passenger))
            .With("ReverseProxy__Clusters__booking__Destinations__monolith__Address", Url(booking))
            // No monolith in this topology: the catch-all cluster points at Identity.
            .With("ReverseProxy__Clusters__monolith__Destinations__monolith__Address", Url(identity))
            .With("RateLimitOptions__PermitLimit", "10000");
        await StartAsync(
            "gateway",
            "src/Gateway/src",
            "Gateway",
            new Uri(GatewayAddress, "/.well-known/openid-configuration"),
            gateway
        );
    }

    private async Task StartAsync(
        string name,
        string projectDirectory,
        string assembly,
        Uri readiness,
        Dictionary<string, string> environment
    )
    {
        var host = await HostProcess.StartAsync(name, projectDirectory, assembly, readiness, environment, LogDirectory);
        lock (hosts)
        {
            hosts.Add(host);
        }
    }

    private Dictionary<string, string> Service(string name, Uri http, Uri identity, Uri? grpc)
    {
        var environment = Common(http, identity)
            .With("MessageBroker__TransportType", "RabbitMq")
            .With("MessageBroker__RabbitMq__Host", RabbitHost)
            .With(
                "MessageBroker__RabbitMq__Port",
                RabbitPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )
            .With("MessageBroker__RabbitMq__Username", RabbitUser)
            .With("MessageBroker__RabbitMq__Password", RabbitPassword)
            .With("MessageBroker__RabbitMq__VirtualHost", "/")
            .With("MongoOptions__ConnectionString", MongoConnectionString)
            .With("PersistMessageOptions__Interval", "1")
            .With("Kestrel__Endpoints__Http__Url", Url(http));
        if (grpc is not null)
        {
            environment
                .With("Kestrel__Endpoints__Http__Protocols", "Http1")
                .With("Kestrel__Endpoints__Grpc__Url", Url(grpc))
                .With("Kestrel__Endpoints__Grpc__Protocols", "Http2");
        }

        return environment;
    }

    private static Dictionary<string, string> Common(Uri http, Uri identity) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
            .With("ASPNETCORE_ENVIRONMENT", "Development")
            .With("ASPNETCORE_URLS", Url(http))
            .With("Jwt__Authority", Url(identity))
            .With("ObservabilityOptions__UseOTLPExporter", "false")
            .With("ObservabilityOptions__UseAspireOTLPExporter", "false")
            .With("ObservabilityOptions__UsePrometheusExporter", "false");

    private static Uri Http(int port) => new($"http://127.0.0.1:{port}");

    private static string Url(Uri uri) => uri.GetLeftPart(UriPartial.Authority);

    private static string Image(string variable, string fallback) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } image ? image : fallback;
}

internal static class EnvironmentExtensions
{
    public static Dictionary<string, string> With(this Dictionary<string, string> environment, string key, string value)
    {
        environment[key] = value;
        return environment;
    }
}
