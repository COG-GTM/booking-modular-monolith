using System.Net.Sockets;
using Aspire.Hosting.ApplicationModel;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("docker-compose");

// 1. Database Services
var pgUsername = builder.AddParameter("pg-username", "postgres", secret: true);
var pgPassword = builder.AddParameter("pg-password", "postgres", secret: true);

var postgres = builder
    .AddPostgres("postgres", pgUsername, pgPassword)
    .WithImage("postgres", "16")
    .WithEndpoint(
        "tcp",
        e =>
        {
            e.Port = 5432;
            e.TargetPort = 5432;
            e.IsProxied = true;
            e.IsExternal = false;
        }
    )
    .WithArgs("-c", "wal_level=logical", "-c", "max_prepared_transactions=100");

if (builder.ExecutionContext.IsPublishMode)
{
    postgres.WithDataVolume("postgres-data").WithLifetime(ContainerLifetime.Persistent);
}

var mongoUsername = builder.AddParameter("mongo-username", "root", secret: true);
var mongoPassword = builder.AddParameter("mongo-password", "secret", secret: true);

var mongo = builder
    .AddMongoDB("mongo", userName: mongoUsername, password: mongoPassword)
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
        }
    );

if (builder.ExecutionContext.IsPublishMode)
{
    mongo.WithDataVolume("mongo-data").WithLifetime(ContainerLifetime.Persistent);
}

var redis = builder
    .AddRedis("redis")
    .WithImage("redis:latest")
    .WithEndpoint(
        "tcp",
        e =>
        {
            e.Port = 6379;
            e.TargetPort = 6379;
            e.IsProxied = true;
            e.IsExternal = false;
        }
    );

if (builder.ExecutionContext.IsPublishMode)
{
    redis.WithDataVolume("redis-data").WithLifetime(ContainerLifetime.Persistent);
}

var eventstore = builder
    .AddEventStore("eventstore")
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
        }
    )
    .WithEndpoint(port: 1113, targetPort: 1113, name: "tcp", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    eventstore.WithDataVolume("eventstore-data").WithLifetime(ContainerLifetime.Persistent);
}

// 2. Messaging Services
var rabbitmqUsername = builder.AddParameter("rabbitmq-username", "booking", secret: true);
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", "booking", secret: true);

var rabbitmq = builder
    .AddRabbitMQ("rabbitmq", rabbitmqUsername, rabbitmqPassword)
    .WithManagementPlugin()
    .WithEndpoint(
        "tcp",
        e =>
        {
            e.TargetPort = 5672;
            e.Port = 5672;
            e.IsProxied = true;
            e.IsExternal = false;
        }
    )
    .WithEndpoint(
        "management",
        e =>
        {
            e.TargetPort = 15672;
            e.Port = 15672;
            e.IsProxied = true;
            e.IsExternal = true;
        }
    );

if (builder.ExecutionContext.IsPublishMode)
{
    rabbitmq.WithLifetime(ContainerLifetime.Persistent);
}

// // 3. Observability Services
var jaeger = builder
    .AddContainer("jaeger-all-in-one", "jaegertracing/all-in-one")
    .WithEndpoint(
        port: 6831,
        targetPort: 6831,
        name: "agent",
        protocol: ProtocolType.Udp,
        isProxied: true,
        isExternal: false
    )
    .WithEndpoint(port: 16686, targetPort: 16686, name: "http", isProxied: true, isExternal: true)
    .WithEndpoint(port: 14268, targetPort: 14268, name: "collector", isProxied: true, isExternal: false)
    .WithEndpoint(port: 14317, targetPort: 4317, name: "otlp-grpc", isProxied: true, isExternal: false)
    .WithEndpoint(port: 14318, targetPort: 4318, name: "otlp-http", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    jaeger.WithLifetime(ContainerLifetime.Persistent);
}

var zipkin = builder
    .AddContainer("zipkin-all-in-one", "openzipkin/zipkin")
    .WithEndpoint(port: 9411, targetPort: 9411, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    zipkin.WithLifetime(ContainerLifetime.Persistent);
}

var otelCollector = builder
    .AddContainer("otel-collector", "otel/opentelemetry-collector-contrib")
    .WithBindMount(
        "../../../../deployments/configs/otel-collector-config.yaml",
        "/etc/otelcol-contrib/config.yaml",
        isReadOnly: true
    )
    .WithArgs("--config=/etc/otelcol-contrib/config.yaml")
    .WithEndpoint(port: 11888, targetPort: 1888, name: "otel-pprof", isProxied: true, isExternal: true)
    .WithEndpoint(port: 8888, targetPort: 8888, name: "otel-metrics", isProxied: true, isExternal: true)
    .WithEndpoint(port: 8889, targetPort: 8889, name: "otel-exporter-metrics", isProxied: true, isExternal: true)
    .WithEndpoint(port: 13133, targetPort: 13133, name: "otel-health", isProxied: true, isExternal: true)
    .WithEndpoint(port: 4317, targetPort: 4317, scheme: "http", name: "otel-grpc", isProxied: true, isExternal: true)
    .WithEndpoint(port: 4318, targetPort: 4318, name: "otel-http", isProxied: true, isExternal: true)
    .WithEndpoint(port: 55679, targetPort: 55679, name: "otel-zpages", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    otelCollector.WithLifetime(ContainerLifetime.Persistent);
}

var prometheus = builder
    .AddContainer("prometheus", "prom/prometheus")
    .WithBindMount("../../../../deployments/configs/prometheus.yaml", "/etc/prometheus/prometheus.yml")
    .WithArgs(
        "--config.file=/etc/prometheus/prometheus.yml",
        "--storage.tsdb.path=/prometheus",
        "--web.console.libraries=/usr/share/prometheus/console_libraries",
        "--web.console.templates=/usr/share/prometheus/consoles",
        "--web.enable-remote-write-receiver"
    )
    .WithEndpoint(port: 9090, targetPort: 9090, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    prometheus.WithLifetime(ContainerLifetime.Persistent);
}

var grafana = builder
    .AddContainer("grafana", "grafana/grafana")
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

var nodeExporter = builder
    .AddContainer("node-exporter", "prom/node-exporter")
    .WithBindMount("/proc", "/host/proc", isReadOnly: true)
    .WithBindMount("/sys", "/host/sys", isReadOnly: true)
    .WithBindMount("/", "/rootfs", isReadOnly: true)
    .WithArgs("--path.procfs=/host/proc", "--path.rootfs=/rootfs", "--path.sysfs=/host/sys")
    .WithEndpoint(port: 9101, targetPort: 9100, name: "http", isProxied: true, isExternal: true);

if (builder.ExecutionContext.IsPublishMode)
{
    nodeExporter.WithLifetime(ContainerLifetime.Persistent);
}

var tempo = builder
    .AddContainer("tempo", "grafana/tempo", "2.6.1")
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

var loki = builder
    .AddContainer("loki", "grafana/loki")
    .WithBindMount("../../../../deployments/configs/loki-config.yaml", "/etc/loki/local-config.yaml", isReadOnly: true)
    .WithArgs("-config.file=/etc/loki/local-config.yaml")
    .WithEndpoint(port: 3100, targetPort: 3100, name: "http", isProxied: true, isExternal: false)
    .WithEndpoint(port: 9096, targetPort: 9096, name: "grpc", isProxied: true, isExternal: false);

if (builder.ExecutionContext.IsPublishMode)
{
    loki.WithLifetime(ContainerLifetime.Persistent);
}

var elasticsearch = builder
    .AddElasticsearch("elasticsearch")
    .WithImageRegistry("docker.elastic.co")
    .WithImage("elasticsearch/elasticsearch", "8.17.0")
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
        }
    )
    .WithEndpoint(
        "internal",
        e =>
        {
            e.TargetPort = 9300;
            e.Port = 9300;
            e.IsProxied = true;
            e.IsExternal = false;
        }
    )
    .WithDataVolume("elastic-data");

if (builder.ExecutionContext.IsPublishMode)
{
    elasticsearch.WithLifetime(ContainerLifetime.Persistent);
}

var kibana = builder
    .AddContainer("kibana", "docker.elastic.co/kibana/kibana:8.17.0")
    .WithEnvironment("ELASTICSEARCH_HOSTS", "http://elasticsearch:9200")
    .WithEndpoint(port: 5601, targetPort: 5601, name: "http", isProxied: true, isExternal: true)
    .WithReference(elasticsearch)
    .WaitFor(elasticsearch);

if (builder.ExecutionContext.IsPublishMode)
{
    kibana.WithLifetime(ContainerLifetime.Persistent);
}

jaeger.WithContainerNetworkAlias("jaeger-all-in-one");
zipkin.WithContainerNetworkAlias("zipkin-all-in-one");
tempo.WithContainerNetworkAlias("tempo");
loki.WithContainerNetworkAlias("loki");
prometheus.WithContainerNetworkAlias("prometheus");
elasticsearch.WithContainerNetworkAlias("elasticsearch");

var topology = builder.Configuration["AppHost:Topology"] ?? "Services";
var gateway = builder
    .AddProject<Gateway>("gateway", options => options.ExcludeLaunchProfile = true)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithHttpEndpoint(port: 5000, name: "http", isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithEnvironment("Jwt__Authority", GatewayPublicUrl);

if (string.Equals(topology, "Services", StringComparison.OrdinalIgnoreCase))
{
    var flightDb = postgres.AddDatabase("flight-service-db", "flight_service");
    var passengerDb = postgres.AddDatabase("passenger-service-db", "passenger_service");
    var identityDb = postgres.AddDatabase("identity-service-db", "identity_service");
    var bookingDb = postgres.AddDatabase("booking-service-db", "booking_service");
    var identity = ConfigureService(
            builder.AddProject<Identity_Host>("identity", options => options.ExcludeLaunchProfile = true),
            identityDb,
            "Identity",
            5103,
            "http"
        )
        .WithEnvironment("AuthOptions__IssuerUri", GatewayPublicUrl);
    var flight = ConfigureService(
        builder.AddProject<Flight_Host>("flight", options => options.ExcludeLaunchProfile = true),
        flightDb,
        "Flight",
        5107,
        "Http"
    );
    var passenger = ConfigureService(
        builder.AddProject<Passenger_Host>("passenger", options => options.ExcludeLaunchProfile = true),
        passengerDb,
        "Passenger",
        5102,
        "Http"
    );
    var booking = ConfigureService(
        builder.AddProject<Booking_Host>("booking", options => options.ExcludeLaunchProfile = true),
        bookingDb,
        "Booking",
        5004,
        "Http"
    );

    foreach (var service in new[] { flight, passenger, booking })
    {
        service
            .WithReference(mongo)
            .WaitFor(mongo)
            .WithEnvironment("MongoOptions__ConnectionString", mongo.Resource.ConnectionStringExpression);
    }

    booking
        .WithReference(eventstore)
        .WaitFor(eventstore)
        .WithEnvironment("EventStoreOptions__ConnectionString", eventstore.Resource.ConnectionStringExpression)
        .WithEnvironment("services__flight__grpc__0", flight.GetEndpoint("Grpc"))
        .WithEnvironment("services__passenger__grpc__0", passenger.GetEndpoint("Grpc"));

    var metadata = ReferenceExpression.Create($"{identity.GetEndpoint("http")}/.well-known/openid-configuration");
    foreach (var service in new[] { identity, flight, passenger, booking, gateway })
        service.WithEnvironment("Jwt__MetadataAddress", metadata);

    foreach (
        var (name, service, endpoint) in new[]
        {
            ("identity", identity, "http"),
            ("flight", flight, "Http"),
            ("passenger", passenger, "Http"),
            ("booking", booking, "Http"),
        }
    )
    {
        gateway
            .WithReference(service)
            .WaitFor(service)
            .WithEnvironment(
                $"ReverseProxy__Clusters__{name}__Destinations__primary__Address",
                service.GetEndpoint(endpoint)
            );
    }
}
else if (string.Equals(topology, "Monolith", StringComparison.OrdinalIgnoreCase))
{
    grafana.WithEndpoint("http", endpoint => endpoint.Port = 3005);
    var flightDb = postgres.AddDatabase("flight-db", "flight");
    var passengerDb = postgres.AddDatabase("passenger-db", "passenger");
    var identityDb = postgres.AddDatabase("identity-db", "identity");
    var bookingDb = postgres.AddDatabase("booking-db", "booking");
    var api = builder
        .AddProject<Api>("api", options => options.ExcludeLaunchProfile = true)
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithReference(bookingDb, "booking")
        .WaitFor(bookingDb)
        .WithReference(flightDb, "flight")
        .WaitFor(flightDb)
        .WithReference(passengerDb, "passenger")
        .WaitFor(passengerDb)
        .WithReference(identityDb, "identity")
        .WaitFor(identityDb)
        .WithReference(mongo)
        .WaitFor(mongo)
        .WithReference(eventstore)
        .WaitFor(eventstore)
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq)
        .WithEnvironment("AuthOptions__IssuerUri", GatewayPublicUrl)
        .WithEnvironment("Jwt__Authority", GatewayPublicUrl);
    foreach (var endpoint in api.Resource.Annotations.OfType<EndpointAnnotation>().ToList())
        api.Resource.Annotations.Remove(endpoint);
    api.WithHttpEndpoint(port: 3001, name: "Http", isProxied: false)
        .WithHttpHealthCheck("/health", endpointName: "Http")
        .WithHttpsEndpoint(port: 3000, name: "Https", isProxied: false)
        .WithHttpEndpoint(port: 3002, name: "Grpc", isProxied: false);
    var http = api.GetEndpoint("Http");
    var metadata = ReferenceExpression.Create($"{http}/.well-known/openid-configuration");
    api.WithEnvironment("Jwt__MetadataAddress", metadata)
        .WithEnvironment("services__flight__grpc__0", api.GetEndpoint("Grpc"))
        .WithEnvironment("services__passenger__grpc__0", api.GetEndpoint("Grpc"));
    gateway.WithReference(api).WaitFor(api).WithEnvironment("Jwt__MetadataAddress", metadata);
    foreach (var cluster in new[] { "identity", "flight", "passenger", "booking", "monolith" })
        gateway.WithEnvironment($"ReverseProxy__Clusters__{cluster}__Destinations__primary__Address", http);
}
else
{
    throw new ArgumentException($"Unknown AppHost:Topology '{topology}'. Expected Services or Monolith.");
}

IResourceBuilder<ProjectResource> ConfigureService(
    IResourceBuilder<ProjectResource> service,
    IResourceBuilder<PostgresDatabaseResource> database,
    string module,
    int port,
    string httpEndpoint
)
{
    service
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
        .WithReference(database, module.ToLowerInvariant())
        .WaitFor(database)
        .WithReference(database, $"{module.ToLowerInvariant()}-persist-message")
        .WithEnvironment($"PostgresOptions__ConnectionString__{module}", database.Resource.ConnectionStringExpression)
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq)
        .WithEnvironment("Jwt__Authority", GatewayPublicUrl);
    foreach (var endpoint in service.Resource.Annotations.OfType<EndpointAnnotation>().ToList())
        service.Resource.Annotations.Remove(endpoint);
    service.WithHttpEndpoint(port: port, name: httpEndpoint, isProxied: false);
    if (module is "Flight" or "Passenger")
        service
            .WithHttpEndpoint(port: module == "Flight" ? 5106 : 5202, name: "Grpc", isProxied: false)
            .WithEnvironment("Kestrel__Endpoints__Grpc__Protocols", "Http2");
    return service.WithHttpHealthCheck(module == "Identity" ? "/health" : "/health/ready", endpointName: httpEndpoint);
}

foreach (var project in builder.Resources.OfType<ProjectResource>().ToList())
{
    if (builder.ExecutionContext.IsPublishMode)
    {
        var endpoints = project.Annotations.OfType<EndpointAnnotation>().ToList();
        var resource = builder.CreateResourceBuilder(project);
        foreach (var endpoint in endpoints)
        {
            endpoint.TargetPort ??= endpoint.Port;
            var url = $"{endpoint.UriScheme}://0.0.0.0:{endpoint.TargetPort}";
            resource.WithEnvironment(
                project.Name is "identity" or "gateway" ? "Urls" : $"Kestrel__Endpoints__{endpoint.Name}__Url",
                url
            );
            if (endpoint.Name == "Grpc")
                resource.WithEnvironment("Kestrel__Endpoints__Grpc__Protocols", "Http2");
        }
    }
    builder
        .CreateResourceBuilder(project)
        .WithEnvironment("ObservabilityOptions__ServiceName", project.Name)
        .WithEnvironment(
            "ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint",
            otelCollector.GetEndpoint("otel-grpc")
        );
}

builder.Build().Run();

public partial class Program
{
    private const string GatewayPublicUrl = "http://localhost:5000";
}
