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


var flightDb = postgres.AddDatabase("flight");
var passengerDb = postgres.AddDatabase("passenger");
var identityDb = postgres.AddDatabase("identity");
var persistMessageDb = postgres.AddDatabase("persist-message");

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


// EventStoreDB runs in secure mode: TLS with a generated CA/node certificate and a generated admin password.
var eventstoreAdminPassword = builder.AddParameter(
    "eventstore-admin-password",
    new GenerateParameterDefault { MinLength = 24, Special = false },
    secret: true,
    persist: true);
var eventstoreOpsPassword = builder.AddParameter(
    "eventstore-ops-password",
    new GenerateParameterDefault { MinLength = 24, Special = false },
    secret: true,
    persist: true);

const string eventstoreCertsTarget = "/certs";
const string eventstoreCaTarget = "/eventstore-ca";
var eventstoreCertsHostPath = Path.Combine(builder.AppHostDirectory, ".eventstore", "certs");

var eventstoreCerts = builder.AddContainer("eventstore-certs", "eventstore/es-gencert-cli", "1.0.2")
    .WithEntrypoint("bash")
    .WithArgs(
        "-c",
        $$"""
        set -e
        if [ ! -f {{eventstoreCertsTarget}}/node/node.crt ]; then
          rm -rf {{eventstoreCertsTarget}}/ca {{eventstoreCertsTarget}}/node
          es-gencert-cli create-ca -out {{eventstoreCertsTarget}}/ca
          es-gencert-cli create-node -ca-certificate {{eventstoreCertsTarget}}/ca/ca.crt -ca-key {{eventstoreCertsTarget}}/ca/ca.key -out {{eventstoreCertsTarget}}/node -ip-addresses 127.0.0.1 -dns-names localhost,eventstore
          rm -f {{eventstoreCertsTarget}}/ca/ca.key
        fi
        if [ -d {{eventstoreCaTarget}} ]; then cp {{eventstoreCertsTarget}}/ca/ca.crt {{eventstoreCaTarget}}/ca.crt; fi
        chown -R 1000:1000 {{eventstoreCertsTarget}}
        """)
    .WithContainerRuntimeArgs("--user", "root")
    .PublishAsDockerComposeService((_, service) => service.User = "root");

var eventstore = builder.AddContainer("eventstore", "eventstore/eventstore", "24.10")
    .WithEnvironment("EVENTSTORE_CLUSTER_SIZE", "1")
    .WithEnvironment("EVENTSTORE_RUN_PROJECTIONS", "All")
    .WithEnvironment("EVENTSTORE_START_STANDARD_PROJECTIONS", "True")
    .WithEnvironment("EVENTSTORE_NODE_PORT", "2113")
    .WithEnvironment("EVENTSTORE_CERTIFICATE_FILE", $"{eventstoreCertsTarget}/node/node.crt")
    .WithEnvironment("EVENTSTORE_CERTIFICATE_PRIVATE_KEY_FILE", $"{eventstoreCertsTarget}/node/node.key")
    .WithEnvironment("EVENTSTORE_TRUSTED_ROOT_CERTIFICATES_PATH", $"{eventstoreCertsTarget}/ca")
    .WithEnvironment("EVENTSTORE_DEFAULT_ADMIN_PASSWORD", eventstoreAdminPassword)
    .WithEnvironment("EVENTSTORE_DEFAULT_OPS_PASSWORD", eventstoreOpsPassword)
    .WithEndpoint(
        "https",
        e =>
        {
            e.UriScheme = "https";
            e.TargetPort = 2113;
            e.Port = 2113;
            e.IsProxied = true;
            e.IsExternal = false;
        })
    .WaitForCompletion(eventstoreCerts);

var eventstoreEndpoint = eventstore.GetEndpoint("https");
string eventstoreCaFile;

if (builder.ExecutionContext.IsPublishMode)
{
    eventstoreCerts
        .WithVolume("eventstore-certs", eventstoreCertsTarget)
        .WithVolume("eventstore-ca", eventstoreCaTarget);

    eventstore.WithVolume("eventstore-certs", eventstoreCertsTarget, isReadOnly: true)
        .WithVolume("eventstore-data", "/var/lib/eventstore")
        .WithLifetime(ContainerLifetime.Persistent);

    eventstoreCaFile = $"{eventstoreCaTarget}/ca.crt";
}
else
{
    Directory.CreateDirectory(eventstoreCertsHostPath);

    eventstoreCerts.WithBindMount(eventstoreCertsHostPath, eventstoreCertsTarget);
    eventstore.WithBindMount(eventstoreCertsHostPath, eventstoreCertsTarget, isReadOnly: true);

    eventstoreCaFile = Path.Combine(eventstoreCertsHostPath, "ca", "ca.crt");
}

var eventstoreConnectionString = ReferenceExpression.Create(
    $"esdb://admin:{eventstoreAdminPassword}@{eventstoreEndpoint.Property(EndpointProperty.HostAndPort)}?tls=true");

var eventstoreConnection = builder.AddConnectionString("eventstore-connection", eventstoreConnectionString);


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

var api = builder.AddProject<Api>("api")
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
    .WithReference(eventstoreConnection, connectionName: "eventstore")
    .WithEnvironment("EventStoreOptions__TlsCaFile", eventstoreCaFile)
    .WaitFor(eventstore)
    .WithReference(rabbitmq)
    .WaitFor(rabbitmq)
    .WithHttpEndpoint(port: 3001, name: "api-http")
    .WithHttpsEndpoint(port: 3000, name: "api-https");

if (builder.ExecutionContext.IsPublishMode)
{
    api.WithAnnotation(new ContainerMountAnnotation("eventstore-ca", eventstoreCaTarget, ContainerMountType.Volume, isReadOnly: true));
}

builder.Build().Run();