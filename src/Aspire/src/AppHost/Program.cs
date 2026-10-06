using System.Net.Sockets;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("docker-compose");

// 1. Database Services
var pgUsername = builder.AddParameter("pg-username", "postgres", secret: true);
var pgPassword = builder.AddParameter("pg-password", "postgres", secret: true);

var postgres = builder.AddPostgres("postgres", pgUsername, pgPassword)
    .WithImage("postgres:latest")
    .WithEndpoint(
        "tcp",
        e =>
        {
            e.Port = 5432;
            e.TargetPort = 5432;
            e.IsProxied = true;
            e.IsExternal = false;
        })
    .WithArgs(
        "-c",
        "wal_level=logical",
        "-c",
        "max_prepared_transactions=10");

if (builder.ExecutionContext.IsPublishMode)
{
    postgres.WithDataVolume("postgres-data")
        .WithLifetime(ContainerLifetime.Persistent);
}


// One logical database per service; each service's outbox/inbox (persist_message) lives in its own database.
// Resource names carry a "-db" suffix so the service projects own the logical names used by service
// discovery (https://flight, http://_grpc.passenger); consumers still read ConnectionStrings:<service>.
var flightDb = postgres.AddDatabase("flight-db", databaseName: "flight");
var passengerDb = postgres.AddDatabase("passenger-db", databaseName: "passenger");
var identityDb = postgres.AddDatabase("identity-db", databaseName: "identity");
var bookingDb = postgres.AddDatabase("booking-db", databaseName: "booking");

var mongoUsername = builder.AddParameter("mongo-username", "root", secret: true);
var mongoPassword = builder.AddParameter("mongo-password", "secret", secret: true);

var mongo = builder.AddMongoDB("mongo", userName: mongoUsername, password: mongoPassword)
    .WithImage("mongo")
    .WithImageTag("latest")
    .WithEndpoint(
        "tcp",
        e =>
        {
            e.Port = 27017;
            e.TargetPort = 27017;
            e.IsProxied = true;
            e.IsExternal = false;
        });

if (builder.ExecutionContext.IsPublishMode)
{
    mongo.WithDataVolume("mongo-data")
        .WithLifetime(ContainerLifetime.Persistent);
}

// One logical read database per service.
var flightReadDb = mongo.AddDatabase("flight-read");
var passengerReadDb = mongo.AddDatabase("passenger-read");
var bookingReadDb = mongo.AddDatabase("booking-read");


var redis = builder.AddRedis("redis")
    .WithImage("redis:latest")
    .WithEndpoint(
        "tcp",
        e =>
        {
            e.Port = 6379;
            e.TargetPort = 6379;
            e.IsProxied = true;
            e.IsExternal = false;
        });

if (builder.ExecutionContext.IsPublishMode)
{
    redis.WithDataVolume("redis-data")
        .WithLifetime(ContainerLifetime.Persistent);
}


var eventstore = builder.AddEventStore("eventstore")
    .WithImage("eventstore/eventstore")
    .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
    .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")
    .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "True")
    .WithEnvironment("EVENTSTORE_INSECURE", "True")
    .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "True")
    .WithEndpoint(
        "http",
        e =>
        {
            e.TargetPort = 2113;
            e.Port = 2113;
            e.IsProxied = true;
            e.IsExternal = true;
        })
    .WithEndpoint(
        port: 1113,
        targetPort: 1113,
        name: "tcp",
        isProxied: true,
        isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    eventstore.WithDataVolume("eventstore-data")
        .WithLifetime(ContainerLifetime.Persistent);
}

// 2. Messaging Services
var rabbitmqUsername = builder.AddParameter("rabbitmq-username", "guest", secret: true);
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rabbitmqUsername, rabbitmqPassword)
    .WithManagementPlugin()
    .WithEndpoint(
        "tcp",
        e =>
        {
            e.TargetPort = 5672;
            e.Port = 5672;
            e.IsProxied = true;
            e.IsExternal = false;
        })
    .WithEndpoint(
        "management",
        e =>
        {
            e.TargetPort = 15672;
            e.Port = 15672;
            e.IsProxied = true;
            e.IsExternal = true;
        });

if (builder.ExecutionContext.IsPublishMode)
{
    rabbitmq.WithLifetime(ContainerLifetime.Persistent);
}

// // 3. Observability Services
var jaeger = builder.AddContainer("jaeger-all-in-one", "jaegertracing/all-in-one")
    .WithEndpoint(
        port: 6831,
        targetPort: 6831,
        name: "agent",
        protocol: ProtocolType.Udp,
        isProxied: true,
        isExternal: false)
    .WithEndpoint(port: 16686, targetPort: 16686, name: "http", isProxied: true, isExternal: true)
    .WithEndpoint(port: 14268, targetPort: 14268, name: "collector", isProxied: true, isExternal: false)
    .WithEndpoint(port: 14317, targetPort: 4317, name: "otlp-grpc", isProxied: true, isExternal: false)
    .WithEndpoint(port: 14318, targetPort: 4318, name: "otlp-http", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    jaeger.WithLifetime(ContainerLifetime.Persistent);
}

var zipkin = builder.AddContainer("zipkin-all-in-one", "openzipkin/zipkin")
    .WithEndpoint(port: 9411, targetPort: 9411, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    zipkin.WithLifetime(ContainerLifetime.Persistent);
}

var otelCollector = builder.AddContainer("otel-collector", "otel/opentelemetry-collector-contrib")
    .WithBindMount(
        "../../../../deployments/configs/otel-collector-config.yaml",
        "/etc/otelcol-contrib/config.yaml",
        isReadOnly: true)
    .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
    .WithEndpoint(port: 11888, targetPort: 1888, name: "otel-pprof", isProxied: true, isExternal: true)
    .WithEndpoint(port: 8888, targetPort: 8888, name: "otel-metrics", isProxied: true, isExternal: true)
    .WithEndpoint(port: 8889, targetPort: 8889, name: "otel-exporter-metrics", isProxied: true, isExternal: true)
    .WithEndpoint(port: 13133, targetPort: 13133, name: "otel-health", isProxied: true, isExternal: true)
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otel-grpc", isProxied: true, isExternal: true)
    .WithEndpoint(port: 4318, targetPort: 4318, name: "otel-http", isProxied: true, isExternal: true)
    .WithEndpoint(port: 55679, targetPort: 55679, name: "otel-zpages", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    otelCollector.WithLifetime(ContainerLifetime.Persistent);
}

var prometheus = builder.AddContainer("prometheus", "prom/prometheus")
    .WithBindMount("../../../../deployments/configs/prometheus.yaml", "/etc/prometheus/prometheus.yml")
    .WithArgs(
        "--config.file=/etc/prometheus/prometheus.yml",
        "--storage.tsdb.path=/prometheus",
        "--web.console.libraries=/usr/share/prometheus/console_libraries",
        "--web.console.templates=/usr/share/prometheus/consoles",
        "--web.enable-remote-write-receiver")
    .WithEndpoint(port: 9090, targetPort: 9090, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    prometheus.WithLifetime(ContainerLifetime.Persistent);
}

var grafana = builder.AddContainer("grafana", "grafana/grafana")
    .WithEnvironment("GF_INSTALL_PLUGINS", "grafana-clock-panel,grafana-simple-json-datasource")
    .WithEnvironment("GF_SECURITY_ADMIN_USER", "admin")
    .WithEnvironment("GF_SECURITY_ADMIN_PASSWORD", "admin")
    .WithEnvironment("GF_FEATURE_TOGGLES_ENABLE", "traceqlEditor")
    .WithBindMount("../../../../deployments/configs/grafana/provisioning", "/etc/grafana/provisioning")
    .WithBindMount("../../../../deployments/configs/grafana/dashboards", "/var/lib/grafana/dashboards")
    .WithEndpoint(port: 3000, targetPort: 3000, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    grafana.WithLifetime(ContainerLifetime.Persistent);
}

var nodeExporter = builder.AddContainer("node-exporter", "prom/node-exporter")
    .WithBindMount("/proc", "/host/proc", isReadOnly: true)
    .WithBindMount("/sys", "/host/sys", isReadOnly: true)
    .WithBindMount("/", "/rootfs", isReadOnly: true)
    .WithArgs(
        "--path.procfs=/host/proc",
        "--path.rootfs=/rootfs",
        "--path.sysfs=/host/sys")
    .WithEndpoint(port: 9101, targetPort: 9100, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    nodeExporter.WithLifetime(ContainerLifetime.Persistent);
}

var tempo = builder.AddContainer("tempo", "grafana/tempo")
    .WithBindMount("../../../../deployments/configs/tempo.yaml", "/etc/tempo.yaml", isReadOnly: true)
    .WithArgs("--config.file=/etc/tempo.yaml")
    .WithEndpoint(port: 3200, targetPort: 3200, name: "http", isProxied: true, isExternal: false)
    .WithEndpoint(port: 9095, targetPort: 9095, name: "grpc", isProxied: true, isExternal: false)
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otlp-grpc", isProxied: true, isExternal: false)
    .WithEndpoint(port: 4318, targetPort: 4318, name: "otlp-http", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    tempo.WithLifetime(ContainerLifetime.Persistent);
}

var loki = builder.AddContainer("loki", "grafana/loki")
    .WithBindMount("../../../../deployments/configs/loki-config.yaml", "/etc/loki/local-config.yaml", isReadOnly: true)
    .WithArgs("-config.file=/etc/loki/local-config.yaml")
    .WithEndpoint(port: 3100, targetPort: 3100, name: "http", isProxied: true, isExternal: false)
    .WithEndpoint(port: 9096, targetPort: 9096, name: "grpc", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    loki.WithLifetime(ContainerLifetime.Persistent);
}

var elasticsearch = builder.AddElasticsearch("elasticsearch")
    .WithImage("docker.elastic.co/elasticsearch/elasticsearch:8.17.0")
    .WithEnvironment("discovery.type", "single-node")
    .WithEnvironment("cluster.name", "docker-cluster")
    .WithEnvironment("node.name", "docker-node")
    .WithEnvironment("ES_JAVA_OPTS", "-Xms512m -Xmx512m")
    .WithEnvironment("xpack.security.enabled", "false")
    .WithEnvironment("xpack.security.http.ssl.enabled", "false")
    .WithEnvironment("xpack.security.transport.ssl.enabled", "false")
    .WithEnvironment("network.host", "0.0.0.0")
    .WithEnvironment("http.port", "9200")
    .WithEnvironment("transport.host", "localhost")
    .WithEnvironment("bootstrap.memory_lock", "true")
    .WithEnvironment("cluster.routing.allocation.disk.threshold_enabled", "false")
    .WithEndpoint(
        "http",
        e =>
        {
            e.TargetPort = 9200;
            e.Port = 9200;
            e.IsProxied = true;
            e.IsExternal = false;
        })
    .WithEndpoint(
        "internal",
        e =>
        {
            e.TargetPort = 9300;
            e.Port = 9300;
            e.IsProxied = true;
            e.IsExternal = false;
        })
    .WithDataVolume("elastic-data");

if (builder.ExecutionContext.IsPublishMode)
{
    elasticsearch.WithLifetime(ContainerLifetime.Persistent);
}

var kibana = builder.AddContainer("kibana", "docker.elastic.co/kibana/kibana:8.17.0")
    .WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
    .WithEndpoint(port: 5601, targetPort: 5601, name: "http", isProxied: true, isExternal: true)
    .WithReference(elasticsearch)
    .WaitFor(elasticsearch);

if (builder.ExecutionContext.IsPublishMode)
{
    kibana.WithLifetime(ContainerLifetime.Persistent);
}

// 4. Application topology
// "Microservices" (default) runs the gateway in front of the standalone Identity, Flight, Passenger and
// Booking hosts, matching the production layout. "Monolith" runs the gateway in front of src/Api only.
// Select it with AppHost:Topology (e.g. `aspire run -- --AppHost:Topology=Monolith` or AppHost__Topology=Monolith).
var topology = builder.Configuration["AppHost:Topology"];
var runMonolith = string.Equals(topology, "Monolith", StringComparison.OrdinalIgnoreCase);

// Launch-profile endpoints (http/https) would otherwise claim the same host ports as the named endpoints
// pinned below (and the api's https one Grafana's 3000), so they are left on dynamic ports.
var gateway = builder.AddProject<Gateway>("gateway")
    .WithEndpoint("http", endpoint => endpoint.Port = null)
    .WithEndpoint("https", endpoint => endpoint.Port = null)
    .WithHttpEndpoint(port: 5000, name: "gateway-http")
    .WithHttpsEndpoint(port: 5001, name: "gateway-https")
    .WithHttpHealthCheck("/health", endpointName: "gateway-http");

// Default destination per gateway cluster; Gateway:Clusters:<cluster> in the AppHost configuration
// (appsettings / env / user-secrets) repoints a single cluster without touching the others.
Dictionary<string, EndpointReference> clusterDestinations;

if (runMonolith)
{
    var api = builder.AddProject<Api>("api")
        .WithEndpoint("http", endpoint => endpoint.Port = null)
        .WithEndpoint("https", endpoint => endpoint.Port = null)
        .WithReference(flightDb, "flight")
        .WaitFor(flightDb)
        .WithReference(passengerDb, "passenger")
        .WaitFor(passengerDb)
        .WithReference(identityDb, "identity")
        .WaitFor(identityDb)
        .WithReference(bookingDb, "booking")
        .WaitFor(bookingDb)
        .WithReference(mongo)
        .WaitFor(mongo)
        .WithReference(flightReadDb)
        .WithReference(passengerReadDb)
        .WithReference(bookingReadDb)
        .WithReference(eventstore)
        .WaitFor(eventstore)
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq)
        .WithHttpEndpoint(port: 3001, name: "api-http")
        .WithHttpsEndpoint(port: 3002, name: "api-https")
        .WithHttpHealthCheck("/health", endpointName: "api-http");

    // Flight and Passenger are hosted inside the monolith: point Booking's service-discovery names at the api itself.
    foreach (var serviceName in new[] { "flight", "passenger" })
    {
        api.WithEnvironment($"services__{serviceName}__https__0", api.GetEndpoint("api-https"));
    }

    // IdentityServer's issuer and every JWT validator must agree on the same public address.
    var issuer = api.GetEndpoint("api-https");
    var metadataAddress = ReferenceExpression.Create($"{issuer}/.well-known/openid-configuration");
    api.WithEnvironment("AuthOptions__IssuerUri", issuer).WithEnvironment("Jwt__Authority", issuer);

    gateway
        .WithReference(api)
        .WaitFor(api)
        .WithEnvironment("Jwt__Authority", issuer)
        .WithEnvironment("Jwt__MetadataAddress", metadataAddress);

    var apiHttp = api.GetEndpoint("api-http");
    clusterDestinations = new()
    {
        ["flight"] = apiHttp,
        ["passenger"] = apiHttp,
        ["booking"] = apiHttp,
        ["identity"] = apiHttp,
        ["monolith"] = apiHttp,
    };
}
else
{
    // Identity owns IdentityServer: its address is the token issuer and the JWT authority of every other service.
    var identity = builder.AddProject<Identity_Host>("identity")
        .WithReference(identityDb, "identity")
        .WaitFor(identityDb)
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq)
        .WithHttpHealthCheck("/health", endpointName: "http");

    // Jwt:MetadataAddress is set explicitly because the hosts' docker appsettings point it at the monolith.
    var issuer = identity.GetEndpoint("http");
    var metadataAddress = ReferenceExpression.Create($"{issuer}/.well-known/openid-configuration");
    identity
        .WithEnvironment("AuthOptions__IssuerUri", issuer)
        .WithEnvironment("Jwt__Authority", issuer)
        .WithEnvironment("Jwt__MetadataAddress", metadataAddress);

    var flight = builder.AddProject<Flight_Host>("flight")
        .WithReference(flightDb, "flight")
        .WaitFor(flightDb)
        .WithReference(flightReadDb)
        .WaitFor(mongo)
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq)
        .WithEnvironment("Jwt__Authority", issuer)
        .WithEnvironment("Jwt__MetadataAddress", metadataAddress)
        .WithHttpHealthCheck("/health", endpointName: "http");

    var passenger = builder.AddProject<Passenger_Host>("passenger")
        .WithReference(passengerDb, "passenger")
        .WaitFor(passengerDb)
        .WithReference(passengerReadDb)
        .WaitFor(mongo)
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq)
        .WithEnvironment("Jwt__Authority", issuer)
        .WithEnvironment("Jwt__MetadataAddress", metadataAddress)
        .WithHttpHealthCheck("/health", endpointName: "http");

    // Booking reaches Flight and Passenger over gRPC by logical name; WithReference injects
    // services__<name>__<endpoint>__0 so service discovery resolves them without hard-coded addresses.
    // Flight serves gRPC on its HTTPS endpoint (https://flight); Passenger on its dedicated
    // HTTP/2-only "grpc" endpoint (http://_grpc.passenger).
    var booking = builder.AddProject<Booking_Host>("booking")
        .WithReference(bookingDb, "booking")
        .WaitFor(bookingDb)
        .WithReference(bookingReadDb)
        .WaitFor(mongo)
        .WithReference(eventstore)
        .WaitFor(eventstore)
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq)
        .WithReference(flight)
        .WaitFor(flight)
        .WithReference(passenger)
        .WaitFor(passenger)
        .WithEnvironment("Grpc__Flight__Address", "https://flight")
        .WithEnvironment("Grpc__Passenger__Address", "http://_grpc.passenger")
        .WithEnvironment("Jwt__Authority", issuer)
        .WithEnvironment("Jwt__MetadataAddress", metadataAddress)
        .WithHttpHealthCheck("/health", endpointName: "http");

    gateway
        .WithReference(identity)
        .WaitFor(identity)
        .WithReference(flight)
        .WaitFor(flight)
        .WithReference(passenger)
        .WaitFor(passenger)
        .WithReference(booking)
        .WaitFor(booking)
        .WithEnvironment("Jwt__Authority", issuer)
        .WithEnvironment("Jwt__MetadataAddress", metadataAddress);

    clusterDestinations = new()
    {
        ["flight"] = flight.GetEndpoint("http"),
        ["passenger"] = passenger.GetEndpoint("http"),
        ["booking"] = booking.GetEndpoint("http"),
        ["identity"] = issuer,
        // No monolith in this topology: non-module routes (e.g. "/") fall through to the Identity host.
        ["monolith"] = issuer,
    };
}

// The gateway's appsettings name each cluster's single destination "monolith"; overriding that destination's
// address (rather than adding a second one) keeps exactly one destination per cluster.
foreach (var (cluster, destination) in clusterDestinations)
{
    var key = $"ReverseProxy__Clusters__{cluster}__Destinations__monolith__Address";
    var overrideAddress = builder.Configuration[$"Gateway:Clusters:{cluster}"];

    if (string.IsNullOrWhiteSpace(overrideAddress))
        gateway.WithEnvironment(key, destination);
    else
        gateway.WithEnvironment(key, overrideAddress);
}

builder.Build().Run();
