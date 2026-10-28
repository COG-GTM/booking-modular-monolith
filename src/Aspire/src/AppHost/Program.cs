using System.Net.Sockets;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("docker-compose");

// 1. Database Services
var pgUsername = builder.AddParameter("pg-username", "postgres", secret: true);
var pgPassword = builder.AddParameter("pg-password", "postgres", secret: true);

var postgres = builder.AddPostgres("postgres", pgUsername, pgPassword)
    .WithImage("postgres")
    .WithImageTag("16")
    .WithBindMount("../../../../deployments/docker-compose/init/postgres", "/docker-entrypoint-initdb.d", isReadOnly: true)
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
        "max_prepared_transactions=100");

if (builder.ExecutionContext.IsPublishMode)
{
    postgres.WithDataVolume("postgres-data")
        .WithLifetime(ContainerLifetime.Persistent);
}




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
    .WithEndpoint(port: 3030, targetPort: 3000, name: "http", isProxied: true, isExternal: true);

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
    .WithImageTag("2.6.1")
    .WithBindMount("../../../../deployments/configs/tempo.yaml", "/etc/tempo.yaml", isReadOnly: true)
    .WithArgs("--config.file=/etc/tempo.yaml")
    .WithEndpoint(port: 3200, targetPort: 3200, name: "http", isProxied: true, isExternal: false)
    .WithEndpoint(port: 9095, targetPort: 9095, name: "grpc", isProxied: true, isExternal: false)
    .WithEndpoint(targetPort: 4317, name: "otlp-grpc", isProxied: true, isExternal: false)
    .WithEndpoint(targetPort: 4318, name: "otlp-http", isProxied: true, isExternal: false);

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
    .WithImageRegistry("docker.elastic.co")
    .WithImage("elasticsearch/elasticsearch")
    .WithImageTag("8.17.0")
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

const int gatewayPort = 5000;
var gatewayPublicUrl = $"http://localhost:{gatewayPort}";
var topology = builder.Configuration["AppHost:Topology"] ?? "Services";
if (topology.Equals("Monolith", StringComparison.OrdinalIgnoreCase))
{
    var api = builder.AddProject<Api>("api")
        .WithEnvironment("AuthOptions__IssuerUri", gatewayPublicUrl)
        .WithEnvironment("Jwt__Authority", gatewayPublicUrl)
        .WaitFor(postgres)
        .WithReference(mongo)
        .WithEnvironment("MongoOptions__ConnectionString", mongo.Resource.ConnectionStringExpression)
        .WaitFor(mongo)
        .WithReference(eventstore)
        .WaitFor(eventstore)
        .WithReference(rabbitmq)
        .WaitFor(rabbitmq)
        .WithHttpEndpoint(port: 3001, name: "api-http")
        .WithHttpsEndpoint(port: 3000, name: "api-https")
        .WithServiceObservability(otelCollector);

    foreach (var module in new[] { "Flight", "Identity", "Passenger", "Booking" })
    {
        var role = module.ToLowerInvariant() + "_service";
        var endpoint = postgres.GetEndpoint("tcp");
        var host = endpoint.Property(EndpointProperty.Host);
        var port = endpoint.Property(EndpointProperty.Port);
        if (module != "Booking")
        {
            api.WithEnvironment(
                $"PostgresOptions__ConnectionString__{module}",
                ReferenceExpression.Create(
                    $"Host={host};Port={port};Database={role};Username={role};Password={role}"));
        }

        api.WithEnvironment(
            $"PersistMessageOptions__ConnectionString__{module}",
            ReferenceExpression.Create(
                $"Host={host};Port={port};Database={role}_persist_message;Username={role};Password={role}"));
    }

    api.WithEnvironment(
        "Jwt__MetadataAddress",
        ReferenceExpression.Create($"{api.GetEndpoint("api-http")}/.well-known/openid-configuration"));
    api.WithEnvironment("Grpc__FlightAddress", api.GetEndpoint("api-https"));
    api.WithEnvironment("Grpc__PassengerAddress", api.GetEndpoint("api-https"));

    var monolithGateway = builder.AddProject<Gateway>("gateway")
        .WithReference(api)
        .WaitFor(api)
        .WithHttpEndpoint(port: gatewayPort, name: "http")
        .WithEnvironment("Jwt__Authority", gatewayPublicUrl)
        .WithEnvironment(
            "Jwt__MetadataAddress",
            ReferenceExpression.Create($"{api.GetEndpoint("api-http")}/.well-known/openid-configuration"))
        .WithHttpHealthCheck("/health/ready", endpointName: "http")
        .WithServiceObservability(otelCollector);

    foreach (var cluster in new[] { "identity", "flight", "passenger", "booking", "monolith" })
    {
        monolithGateway.WithEnvironment(
            $"ReverseProxy__Clusters__{cluster}__Destinations__primary__Address",
            api.GetEndpoint("api-http"));
    }
}
else if (topology.Equals("Services", StringComparison.OrdinalIgnoreCase))
{
    var postgresEndpoint = postgres.GetEndpoint("tcp");
    var postgresHost = postgresEndpoint.Property(EndpointProperty.Host);
    var postgresPort = postgresEndpoint.Property(EndpointProperty.Port);

    ReferenceExpression PostgresConnectionString(string database, string role) =>
        ReferenceExpression.Create(
            $"Host={postgresHost};Port={postgresPort};Database={database};Username={role};Password={role}");

    var identityConnectionString = PostgresConnectionString("identity_service", "identity_service");
    var identityPersistMessageConnectionString =
        PostgresConnectionString("identity_service_persist_message", "identity_service");
    var flightConnectionString = PostgresConnectionString("flight_service", "flight_service");
    var flightPersistMessageConnectionString =
        PostgresConnectionString("flight_service_persist_message", "flight_service");
    var passengerConnectionString = PostgresConnectionString("passenger_service", "passenger_service");
    var passengerPersistMessageConnectionString =
        PostgresConnectionString("passenger_service_persist_message", "passenger_service");
    var bookingPersistMessageConnectionString =
        PostgresConnectionString("booking_service_persist_message", "booking_service");

    var identityDatabase = builder.AddConnectionString("identity", identityConnectionString);
    var identityPersistMessageDatabase =
        builder.AddConnectionString("identity-persist-message", identityPersistMessageConnectionString);
    var flightDatabase = builder.AddConnectionString("flight", flightConnectionString);
    var flightPersistMessageDatabase =
        builder.AddConnectionString("flight-persist-message", flightPersistMessageConnectionString);
    var passengerDatabase = builder.AddConnectionString("passenger", passengerConnectionString);
    var passengerPersistMessageDatabase =
        builder.AddConnectionString("passenger-persist-message", passengerPersistMessageConnectionString);
    var bookingPersistMessageDatabase =
        builder.AddConnectionString("booking-persist-message", bookingPersistMessageConnectionString);

    var identity = builder.AddProject<Identity_Host>(
            "identity-service",
            options =>
            {
                options.ExcludeLaunchProfile = true;
                options.ExcludeKestrelEndpoints = true;
            })
        .WithHttpEndpoint(targetPort: 5101, port: 5101, name: "http", isProxied: false)
        .WithReference(identityDatabase)
        .WithReference(identityPersistMessageDatabase)
        .WithEnvironment("PostgresOptions__ConnectionString__Identity", identityConnectionString)
        .WithEnvironment(
            "PersistMessageOptions__ConnectionString__Identity",
            identityPersistMessageConnectionString)
        .WithReference(rabbitmq)
        .WaitFor(postgres)
        .WaitFor(rabbitmq)
        .WithEnvironment("AuthOptions__IssuerUri", gatewayPublicUrl)
        .WithEnvironment("Jwt__Authority", gatewayPublicUrl)
        .WithEnvironment("Modules__Identity__BackgroundProcessingEnabled", "true")
        .WithServiceObservability(otelCollector)
        .WithHttpHealthCheck("/health/ready", endpointName: "http");

    var identityMetadataAddress =
        ReferenceExpression.Create($"{identity.GetEndpoint("http")}/.well-known/openid-configuration");
    identity.WithEnvironment("Jwt__MetadataAddress", identityMetadataAddress);

    var flight = builder.AddProject<Flight_Host>(
            "flight-service",
            options =>
            {
                options.ExcludeLaunchProfile = true;
                options.ExcludeKestrelEndpoints = true;
            })
        .WithHttpEndpoint(targetPort: 5102, port: 5102, name: "http", isProxied: false)
        .WithEndpoint(
            targetPort: 5112,
            port: 5112,
            scheme: "http",
            name: "grpc",
            isProxied: false,
            isExternal: false)
        .WithReference(flightDatabase)
        .WithReference(flightPersistMessageDatabase)
        .WithEnvironment("PostgresOptions__ConnectionString__Flight", flightConnectionString)
        .WithEnvironment("PersistMessageOptions__ConnectionString__Flight", flightPersistMessageConnectionString)
        .WithReference(mongo)
        .WithEnvironment("MongoOptions__ConnectionString", mongo.Resource.ConnectionStringExpression)
        .WithEnvironment("MongoOptions__Flight__ConnectionString", mongo.Resource.ConnectionStringExpression)
        .WithReference(rabbitmq)
        .WaitFor(postgres)
        .WithEnvironment("Modules__Flight__BackgroundProcessingEnabled", "true")
        .WithEnvironment("Jwt__Authority", gatewayPublicUrl)
        .WithEnvironment("Jwt__MetadataAddress", identityMetadataAddress)
        .WithServiceObservability(otelCollector)
        .WithHttpHealthCheck("/health/ready", endpointName: "http");

    var passenger = builder.AddProject<Passenger_Host>(
            "passenger-service",
            options =>
            {
                options.ExcludeLaunchProfile = true;
                options.ExcludeKestrelEndpoints = true;
            })
        .WithHttpEndpoint(targetPort: 5103, port: 5103, name: "http", isProxied: false)
        .WithEndpoint(
            targetPort: 5113,
            port: 5113,
            scheme: "http",
            name: "grpc",
            isProxied: false,
            isExternal: false)
        .WithReference(passengerDatabase)
        .WithReference(passengerPersistMessageDatabase)
        .WithEnvironment("PostgresOptions__ConnectionString__Passenger", passengerConnectionString)
        .WithEnvironment(
            "PersistMessageOptions__ConnectionString__Passenger",
            passengerPersistMessageConnectionString)
        .WithReference(mongo)
        .WithEnvironment("MongoOptions__Passenger__ConnectionString", mongo.Resource.ConnectionStringExpression)
        .WithReference(rabbitmq)
        .WaitFor(postgres)
        .WaitFor(rabbitmq)
        .WithEnvironment("Modules__Passenger__BackgroundProcessingEnabled", "true")
        .WithEnvironment("Jwt__Authority", gatewayPublicUrl)
        .WithEnvironment("Jwt__MetadataAddress", identityMetadataAddress)
        .WithServiceObservability(otelCollector)
        .WithHttpHealthCheck("/health/ready", endpointName: "http");

    var booking = builder.AddProject<Booking_Host>(
            "booking-service",
            options =>
            {
                options.ExcludeLaunchProfile = true;
                options.ExcludeKestrelEndpoints = true;
            })
        .WithHttpEndpoint(targetPort: 5104, port: 5104, name: "http", isProxied: false)
        .WithReference(bookingPersistMessageDatabase)
        .WithEnvironment(
            "PersistMessageOptions__ConnectionString__Booking",
            bookingPersistMessageConnectionString)
        .WithReference(mongo)
        .WithEnvironment("MongoOptions__ConnectionString", mongo.Resource.ConnectionStringExpression)
        .WithReference(eventstore)
        .WaitFor(eventstore)
        .WithReference(rabbitmq)
        .WithReference(flight)
        .WithReference(passenger)
        .WaitFor(postgres)
        .WaitFor(flight)
        .WaitFor(passenger)
        .WithEnvironment("Grpc__FlightAddress", "http://_grpc.flight-service")
        .WithEnvironment("Grpc__PassengerAddress", "http://_grpc.passenger-service")
        .WithEnvironment("Jwt__Authority", gatewayPublicUrl)
        .WithEnvironment("Jwt__MetadataAddress", identityMetadataAddress)
        .WithEnvironment("Modules__Booking__BackgroundProcessingEnabled", "true")
        .WithServiceObservability(otelCollector)
        .WithHttpHealthCheck("/health/ready", endpointName: "http");

    var gateway = builder.AddProject<Gateway>("gateway")
        .WithHttpEndpoint(port: gatewayPort, name: "http")
        .WithReference(identity)
        .WithReference(flight)
        .WithReference(passenger)
        .WithReference(booking)
        .WaitFor(identity)
        .WaitFor(flight)
        .WaitFor(passenger)
        .WaitFor(booking)
        .WithEnvironment("Jwt__Authority", gatewayPublicUrl)
        .WithEnvironment("Jwt__MetadataAddress", identityMetadataAddress)
        .WithHttpHealthCheck("/health/ready", endpointName: "http")
        .WithServiceObservability(otelCollector);

    foreach (var (cluster, project) in new[]
    {
        ("identity", identity),
        ("flight", flight),
        ("passenger", passenger),
        ("booking", booking)
    })
    {
        gateway.WithEnvironment(
            $"ReverseProxy__Clusters__{cluster}__Destinations__primary__Address",
            project.GetEndpoint("http"));
    }

    gateway.WithEnvironment(
        "ReverseProxy__Clusters__monolith__Destinations__primary__Address",
        identity.GetEndpoint("http"));
    // The Identity-backed catch-all must not expose unmatched paths anonymously.
    gateway.WithEnvironment(
        "ReverseProxy__Routes__fallback__AuthorizationPolicy",
        "authenticated");
}
else
{
    throw new InvalidOperationException(
        $"Unsupported AppHost topology '{topology}'. Expected 'Services' or 'Monolith'.");
}

builder.Build().Run();