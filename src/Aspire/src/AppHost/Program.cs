using System.Net.Sockets;
using Aspire.Hosting.Docker.Resources.ServiceNodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("docker-compose");

// 1. Database Services
var pgUsername = builder.AddParameter("pg-username", "postgres", secret: true);
var pgPassword = builder.AddParameter("pg-password", "postgres", secret: true);

var postgres = builder
    .AddPostgres("postgres", pgUsername, pgPassword)
    .WithImage("postgres:latest")
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
    .WithArgs("-c", "wal_level=logical", "-c", "max_prepared_transactions=10");

if (builder.ExecutionContext.IsPublishMode)
{
    postgres.WithDataVolume("postgres-data").WithLifetime(ContainerLifetime.Persistent);
}

var flightDb = postgres.AddDatabase("flight");
var passengerDb = postgres.AddDatabase("passenger");
var identityDb = postgres.AddDatabase("identity");
var persistMessageDb = postgres.AddDatabase("persist-message");

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

var eventStoreAdminPassword = builder.AddParameter(
    "eventstore-admin-password",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true
);
var eventStoreOpsPassword = builder.AddParameter(
    "eventstore-ops-password",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true
);
var eventStoreCertDirectory = Path.GetFullPath(
    Path.Combine(builder.AppHostDirectory, "../../../../deployments/configs/eventstore/certs")
);
var eventStoreCaFile = builder.ExecutionContext.IsPublishMode
    ? "/etc/eventstore/certs/ca/ca.crt"
    : Path.Combine(eventStoreCertDirectory, "ca", "ca.crt");

var eventstore = builder
    .AddContainer("eventstore", "eventstore/eventstore", "24.10")
    .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
    .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")
    .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "True")
    .WithEnvironment("EVENTSTORE_ENABLE_ATOM_PUB_OVER_HTTP", "True")
    .WithEnvironment("EVENTSTORE_CERTIFICATE_FILE", "/etc/eventstore/certs/node/node.crt")
    .WithEnvironment("EVENTSTORE_CERTIFICATE_PRIVATE_KEY_FILE", "/etc/eventstore/certs/node/node.key")
    .WithEnvironment("EVENTSTORE_TRUSTED_ROOT_CERTIFICATES_PATH", "/etc/eventstore/certs/ca")
    .WithEnvironment("EVENTSTORE_DEFAULT_ADMIN_PASSWORD", eventStoreAdminPassword)
    .WithEnvironment("EVENTSTORE_DEFAULT_OPS_PASSWORD", eventStoreOpsPassword)
    .WithBindMount(eventStoreCertDirectory, "/etc/eventstore/certs", isReadOnly: true)
    .WithEndpoint(
        "https",
        e =>
        {
            e.TargetPort = 2113;
            e.Port = 2113;
            e.UriScheme = "https";
            e.IsProxied = true;
            e.IsExternal = false;
        }
    );

var eventStoreEndpoint = eventstore.GetEndpoint("https");
var eventStoreConnectionString = ReferenceExpression.Create(
    $"esdb://admin:{eventStoreAdminPassword}@{eventStoreEndpoint.Property(EndpointProperty.Host)}:{eventStoreEndpoint.Property(EndpointProperty.Port)}?tls=true&tlsCaFile={eventStoreCaFile}"
);
const string eventStoreHealthCheckName = "eventstore";
builder
    .Services.AddHealthChecks()
    .Add(
        new HealthCheckRegistration(
            eventStoreHealthCheckName,
            _ => new EventStoreHealthCheck(eventStoreEndpoint, eventStoreCaFile),
            failureStatus: null,
            tags: null
        )
    );
eventstore.WithHealthCheck(eventStoreHealthCheckName);

if (builder.ExecutionContext.IsPublishMode)
{
    eventstore.WithVolume("eventstore-data", "/var/lib/eventstore").WithLifetime(ContainerLifetime.Persistent);
    eventstore.PublishAsDockerComposeService(
        (_, service) =>
        {
            var certificateMount = service.Volumes!.Single(volume => volume.Target == "/etc/eventstore/certs");
            certificateMount.Source = eventStoreCertDirectory;
        }
    );
}

// 2. Messaging Services
var rabbitmqUsername = builder.AddParameter("rabbitmq-username", "guest", secret: true);
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);

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
    .WithEndpoint(port: 4317, targetPort: 4317, name: "otel-grpc", isProxied: true, isExternal: true)
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
    .AddContainer("tempo", "grafana/tempo")
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

var api = builder
    .AddProject<Api>("api")
    .WithReference(persistMessageDb)
    .WaitFor(persistMessageDb)
    .WithReference(flightDb)
    .WaitFor(flightDb)
    .WithReference(passengerDb)
    .WaitFor(passengerDb)
    .WithReference(identityDb)
    .WaitFor(identityDb)
    .WithReference(mongo)
    .WaitFor(mongo)
    .WithEnvironment("ConnectionStrings__eventstore", eventStoreConnectionString)
    .WaitFor(eventstore)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithHttpEndpoint(port: 3001, name: "api-http")
    .WithHttpsEndpoint(port: 3000, name: "api-https");

if (builder.ExecutionContext.IsPublishMode)
{
    api.PublishAsDockerComposeService(
        (_, service) =>
            service.AddVolume(
                new Volume
                {
                    Name = "eventstore-ca-certs",
                    Type = "bind",
                    Source = Path.Combine(eventStoreCertDirectory, "ca"),
                    Target = "/etc/eventstore/certs/ca",
                    ReadOnly = true,
                }
            )
    );
}

builder.Build().Run();
