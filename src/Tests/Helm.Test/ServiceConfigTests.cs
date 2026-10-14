using FluentAssertions;
using Xunit;

namespace Helm.Test;

public class ServiceConfigTests
{
    private static string[] Kind => ["-f", HelmChart.ValuesFile("values-kind.yaml")];

    private static string[] Production =>
        ["-f", HelmChart.ValuesFile("values-production.yaml"), "--set", "global.image.tag=abc123"];

    [HelmFact]
    public void every_service_gets_the_jwt_authority_and_in_cluster_metadata_address()
    {
        var configMaps = HelmChart.Render().Where(o => o.Kind == "ConfigMap").ToList();

        configMaps.Should().HaveCount(5);
        foreach (var cm in configMaps)
        {
            var data = cm.Map("data");
            data.Should()
                .Contain("Jwt__Authority", "http://localhost:8080", $"{cm}")
                .And.Contain(
                    "Jwt__MetadataAddress",
                    "http://identity-service/.well-known/openid-configuration",
                    $"{cm}"
                )
                .And.Contain("ASPNETCORE_ENVIRONMENT", "docker", $"{cm}")
                .And.Contain("ObservabilityOptions__UseAspireOTLPExporter", "false", $"{cm}");
        }
    }

    [HelmFact]
    public void only_identity_stamps_the_public_url_as_issuer()
    {
        var configMaps = HelmChart
            .Render("--set", "global.publicUrl=https://booking.example.com")
            .Where(o => o.Kind == "ConfigMap")
            .ToDictionary(o => o.Name, StringComparer.Ordinal);

        configMaps["identity-service-config"]
            .Map("data")
            .Should()
            .Contain("AuthOptions__IssuerUri", "https://booking.example.com")
            .And.Contain("Jwt__Authority", "https://booking.example.com");
        foreach (var name in new[] { "gateway", "flight-service", "passenger-service", "booking-service" })
        {
            configMaps[$"{name}-config"]
                .Map("data")
                .Should()
                .NotContainKey("AuthOptions__IssuerUri", $"{name} only validates tokens")
                .And.Contain("Jwt__Authority", "https://booking.example.com");
        }
    }

    [HelmFact]
    public void service_env_overrides_global_env_which_overrides_computed_env()
    {
        var configMaps = HelmChart
            .Render(
                "--set",
                "global.env.Shared__Key=global",
                "--set",
                "services.gateway.env.Shared__Key=gateway",
                "--set",
                "global.env.Jwt__Authority=http://override"
            )
            .Where(o => o.Kind == "ConfigMap")
            .ToDictionary(o => o.Name, StringComparer.Ordinal);

        configMaps["gateway-config"].Map("data").Should().Contain("Shared__Key", "gateway");
        configMaps["flight-service-config"].Map("data").Should().Contain("Shared__Key", "global");
        configMaps["flight-service-config"].Map("data").Should().Contain("Jwt__Authority", "http://override");
    }

    [HelmFact]
    public void gateway_routes_to_the_in_cluster_service_names()
    {
        var data = HelmChart.Render().Single(o => o.Kind == "ConfigMap" && o.Name == "gateway-config").Map("data");

        data.Should()
            .Contain("ReverseProxy__Clusters__identity__Destinations__monolith__Address", "http://identity-service:80")
            .And.Contain("ReverseProxy__Clusters__flight__Destinations__monolith__Address", "http://flight-service:80")
            .And.Contain(
                "ReverseProxy__Clusters__passenger__Destinations__monolith__Address",
                "http://passenger-service:5102"
            )
            .And.Contain(
                "ReverseProxy__Clusters__booking__Destinations__monolith__Address",
                "http://booking-service:80"
            );
    }

    [HelmFact]
    public void booking_service_resolves_grpc_peers_through_service_discovery()
    {
        var data = HelmChart
            .Render()
            .Single(o => o.Kind == "ConfigMap" && o.Name == "booking-service-config")
            .Map("data");

        data.Should()
            .Contain("Grpc__Flight__Address", "http://flight")
            .And.Contain("Services__flight__http__0", "http://flight-service:81")
            .And.Contain("Grpc__Passenger__Address", "http://passenger")
            .And.Contain("Services__passenger__http__0", "http://passenger-service:5202");
    }

    [HelmFact]
    public void kind_overlay_env_is_rendered_as_quoted_strings_for_every_service()
    {
        var configMaps = HelmChart
            .Render(Kind)
            .Where(o => o.Kind == "ConfigMap" && o.Name.EndsWith("-config", StringComparison.Ordinal))
            .ToList();

        configMaps.Should().HaveCount(5);
        foreach (var cm in configMaps)
        {
            cm.Map("data")
                .Should()
                .Contain("TrustedProxyOptions__KnownNetworks__0", "10.244.0.0/16", $"{cm}")
                .And.Contain("ObservabilityOptions__UseOTLPExporter", "false", $"{cm}");
        }

        foreach (var cm in configMaps.Where(c => c.Name != "gateway-config"))
        {
            cm.Map("data")
                .Should()
                .Contain("RabbitMqOptions__Port", "5672", $"{cm} keeps numeric-looking values as strings");
        }
    }

    [HelmFact]
    public void secrets_hold_the_dev_only_connection_strings_and_broker_credentials()
    {
        var secrets = HelmChart
            .Render()
            .Where(o => o.Kind == "Secret")
            .ToDictionary(o => o.Name, StringComparer.Ordinal);

        foreach (var secret in secrets.Values)
        {
            secret.Get("type").Should().Be("Opaque");
        }

        secrets["gateway-secret"].Map("stringData").Should().BeEmpty();
        secrets["identity-service-secret"]
            .Map("stringData")
            .Should()
            .ContainKey("PostgresOptions__ConnectionString__Identity")
            .And.Contain("RabbitMqOptions__UserName", "guest")
            .And.Contain("RabbitMqOptions__Password", "guest");
        secrets["flight-service-secret"]
            .Map("stringData")
            .Should()
            .ContainKey("PostgresOptions__ConnectionString__Flight")
            .And.Contain("MongoOptions__ConnectionString", "mongodb://mongo:27017");
        secrets["passenger-service-secret"]
            .Map("stringData")
            .Should()
            .ContainKey("PostgresOptions__ConnectionString__Passenger");
        secrets["booking-service-secret"]
            .Map("stringData")
            .Should()
            .ContainKey("PostgresOptions__ConnectionString__Booking")
            .And.Contain("EventStoreOptions__ConnectionString", "esdb://eventstore:2113?tls=false");
    }

    [HelmFact]
    public void existing_secret_replaces_the_rendered_secret_and_its_checksum_annotation()
    {
        var objects = HelmChart.Render("--set", "services.flight-service.existingSecret=flight-from-vault");

        objects.Should().NotContain(o => o.Kind == "Secret" && o.Name == "flight-service-secret");
        objects.Should().Contain(o => o.Kind == "Secret" && o.Name == "identity-service-secret");

        var flight = objects.Single(o => o.Kind == "Deployment" && o.Name == "flight-service");
        flight.Get("spec.template.spec.containers.0.envFrom.1.secretRef.name").Should().Be("flight-from-vault");
        flight.Has("spec.template.metadata.annotations.checksum/secret").Should().BeFalse();
        flight.Has("spec.template.metadata.annotations.checksum/config").Should().BeTrue();

        var identity = objects.Single(o => o.Kind == "Deployment" && o.Name == "identity-service");
        identity.Get("spec.template.spec.containers.0.envFrom.1.secretRef.name").Should().Be("identity-service-secret");
        identity.Has("spec.template.metadata.annotations.checksum/secret").Should().BeTrue();
    }

    [HelmFact]
    public void deployments_use_the_probe_endpoints_and_disable_service_links()
    {
        foreach (var deployment in HelmChart.Render().Where(o => o.Kind == "Deployment"))
        {
            var container = "spec.template.spec.containers.0";
            deployment.Get("spec.template.spec.enableServiceLinks").Should().Be("false", $"{deployment}");
            deployment.Get("spec.replicas").Should().Be("1", $"{deployment}");
            deployment.Get("spec.revisionHistoryLimit").Should().Be("3");
            deployment.Get($"{container}.name").Should().Be(deployment.Name);
            deployment.Get($"{container}.image").Should().Be($"booking/{deployment.Name}:dev");
            deployment.Get($"{container}.imagePullPolicy").Should().Be("IfNotPresent");
            deployment.Get($"{container}.startupProbe.httpGet.path").Should().Be("/alive", $"{deployment}");
            deployment.Get($"{container}.startupProbe.httpGet.port").Should().Be("http");
            deployment.Get($"{container}.startupProbe.failureThreshold").Should().Be("36");
            deployment.Get($"{container}.livenessProbe.httpGet.path").Should().Be("/alive", $"{deployment}");
            deployment.Get($"{container}.readinessProbe.httpGet.path").Should().Be("/ready", $"{deployment}");
            deployment.Get($"{container}.readinessProbe.httpGet.port").Should().Be("http");
            deployment.Get($"{container}.envFrom.0.configMapRef.name").Should().Be($"{deployment.Name}-config");
            deployment.Get($"{container}.envFrom.1.secretRef.name").Should().Be($"{deployment.Name}-secret");
            deployment.Get($"{container}.resources.requests.cpu").Should().Be("100m");
            deployment.Get($"{container}.resources.limits.memory").Should().Be("768Mi");
            deployment
                .Has("spec.template.spec.initContainers")
                .Should()
                .BeFalse("waitForDependencies is off by default");
            deployment.Has("spec.template.spec.volumes").Should().BeFalse("no signing key configured by default");
        }
    }

    [HelmFact]
    public void service_overrides_are_merged_over_chart_defaults()
    {
        var objects = HelmChart.Render(
            "--set",
            "services.flight-service.replicaCount=3",
            "--set",
            "services.flight-service.resources.requests.cpu=500m",
            "--set",
            "services.flight-service.probes.readiness.path=/healthz"
        );

        var flight = objects.Single(o => o.Kind == "Deployment" && o.Name == "flight-service");
        flight.Get("spec.replicas").Should().Be("3");
        flight.Get("spec.template.spec.containers.0.resources.requests.cpu").Should().Be("500m");
        flight
            .Get("spec.template.spec.containers.0.resources.requests.memory")
            .Should()
            .Be("256Mi", "untouched defaults survive the merge");
        flight.Get("spec.template.spec.containers.0.readinessProbe.path").Should().BeNull();
        flight.Get("spec.template.spec.containers.0.readinessProbe.httpGet.path").Should().Be("/healthz");
        flight.Get("spec.template.spec.containers.0.livenessProbe.httpGet.path").Should().Be("/alive");

        objects.Single(o => o.Kind == "Deployment" && o.Name == "gateway").Get("spec.replicas").Should().Be("1");
    }

    [HelmFact]
    public void production_images_are_prefixed_with_the_registry_and_tagged_from_global_tag()
    {
        var deployments = HelmChart.Render(Production).Where(o => o.Kind == "Deployment").ToList();

        deployments.Should().HaveCount(5);
        foreach (var deployment in deployments)
        {
            deployment
                .Get("spec.template.spec.containers.0.image")
                .Should()
                .Be($"ghcr.io/cog-gtm/booking-modular-monolith/{deployment.Name}:abc123");
            deployment
                .Get("spec.template.spec.containers.0.envFrom.1.secretRef.name")
                .Should()
                .Be($"{deployment.Name}-secret");
            deployment
                .Get("spec.template.spec.containers.0.securityContext.allowPrivilegeEscalation")
                .Should()
                .Be("false");
            deployment
                .Get(
                    "spec.template.spec.affinity.podAntiAffinity.preferredDuringSchedulingIgnoredDuringExecution.0.weight"
                )
                .Should()
                .Be("100");
            deployment.Has("spec.replicas").Should().BeFalse("the HPA owns the replica count");
        }
    }

    [HelmFact]
    public void service_image_tag_wins_over_global_tag_and_registry_trailing_slash_is_trimmed()
    {
        var objects = HelmChart.Render(
            "--set",
            "global.image.registry=registry.local/",
            "--set",
            "global.image.tag=1.2.3",
            "--set",
            "services.gateway.image.tag=pinned"
        );

        objects
            .Single(o => o.Kind == "Deployment" && o.Name == "gateway")
            .Get("spec.template.spec.containers.0.image")
            .Should()
            .Be("registry.local/booking/gateway:pinned");
        objects
            .Single(o => o.Kind == "Deployment" && o.Name == "flight-service")
            .Get("spec.template.spec.containers.0.image")
            .Should()
            .Be("registry.local/booking/flight-service:1.2.3");
    }

    [HelmFact]
    public void kind_overlay_never_pulls_images_and_waits_for_dependencies()
    {
        var deployments = HelmChart
            .Render(Kind)
            .Where(o => o.Kind == "Deployment")
            .ToDictionary(o => o.Name, StringComparer.Ordinal);

        deployments["booking-service"].Get("spec.template.spec.containers.0.imagePullPolicy").Should().Be("Never");

        var init = deployments["booking-service"]
            .List("spec.template.spec.initContainers")
            .Should()
            .ContainSingle()
            .Subject;
        K8sObject.Scalar(init, "name").Should().Be("wait-for-dependencies");
        K8sObject.Scalar(init, "image").Should().Be("busybox:1.36");
        var script = deployments["booking-service"].Get("spec.template.spec.initContainers.0.command.2");
        script
            .Should()
            .Contain(
                "for target in postgres:5432 mongo:27017 eventstore:2113 rabbitmq:5672 flight-service:81 passenger-service:5202;"
            )
            .And.Contain("nc -z");

        deployments["gateway"].Has("spec.template.spec.initContainers").Should().BeFalse("gateway has no dependsOn");
    }

    [HelmFact]
    public void identity_signing_key_is_mounted_from_the_existing_secret()
    {
        var objects = HelmChart.Render(Kind);
        var identity = objects.Single(o => o.Kind == "Deployment" && o.Name == "identity-service");

        identity.Get("spec.template.spec.containers.0.volumeMounts.0.name").Should().Be("signing-key");
        identity.Get("spec.template.spec.containers.0.volumeMounts.0.mountPath").Should().Be("/app/tempkey.jwk");
        identity.Get("spec.template.spec.containers.0.volumeMounts.0.subPath").Should().Be("tempkey.jwk");
        identity.Get("spec.template.spec.containers.0.volumeMounts.0.readOnly").Should().Be("true");
        identity.Get("spec.template.spec.volumes.0.name").Should().Be("signing-key");
        identity.Get("spec.template.spec.volumes.0.secret.secretName").Should().Be("identity-signing-key");
        identity.Get("spec.template.spec.volumes.0.secret.items.0.key").Should().Be("tempkey.jwk");
        identity.Get("spec.template.spec.volumes.0.secret.items.0.path").Should().Be("tempkey.jwk");

        objects
            .Where(o =>
                o.Kind == "Deployment"
                && o.Name != "identity-service"
                && o.Map("metadata.labels")["app.kubernetes.io/component"] == "service"
            )
            .Should()
            .OnlyContain(d => !d.Has("spec.template.spec.volumes"), "only identity mounts a signing key");
    }

    [HelmFact]
    public void signing_key_secret_key_name_is_configurable()
    {
        var identity = HelmChart
            .Render(
                "--set",
                "services.identity-service.signingKey.existingSecret=my-key",
                "--set",
                "services.identity-service.signingKey.key=signing.jwk"
            )
            .Single(o => o.Kind == "Deployment" && o.Name == "identity-service");

        identity.Get("spec.template.spec.volumes.0.secret.secretName").Should().Be("my-key");
        identity.Get("spec.template.spec.volumes.0.secret.items.0.key").Should().Be("signing.jwk");
        identity
            .Get("spec.template.spec.volumes.0.secret.items.0.path")
            .Should()
            .Be("tempkey.jwk", "the app always reads tempkey.jwk");
    }

    [HelmFact]
    public void hpa_targets_its_deployment_and_omits_the_memory_metric_unless_configured()
    {
        var objects = HelmChart.Render(
            Kind.Concat(["--set", "services.flight-service.autoscaling.targetMemoryUtilizationPercentage=60"]).ToArray()
        );
        var hpas = objects
            .Where(o => o.Kind == "HorizontalPodAutoscaler")
            .ToDictionary(o => o.Name, StringComparer.Ordinal);

        foreach (var hpa in hpas.Values)
        {
            hpa.ApiVersion.Should().Be("autoscaling/v2");
            hpa.Get("spec.scaleTargetRef.kind").Should().Be("Deployment");
            hpa.Get("spec.scaleTargetRef.name").Should().Be(hpa.Name);
            hpa.Get("spec.minReplicas").Should().Be("1");
            hpa.Get("spec.maxReplicas").Should().Be("2");
            hpa.Get("spec.metrics.0.resource.name").Should().Be("cpu");
            hpa.Get("spec.metrics.0.resource.target.averageUtilization").Should().Be("80");
        }

        hpas["gateway"].List("spec.metrics").Should().HaveCount(1);
        hpas["flight-service"].List("spec.metrics").Should().HaveCount(2);
        hpas["flight-service"].Get("spec.metrics.1.resource.name").Should().Be("memory");
        hpas["flight-service"].Get("spec.metrics.1.resource.target.averageUtilization").Should().Be("60");

        objects
            .Where(o => o.Kind == "Deployment" && hpas.ContainsKey(o.Name))
            .Should()
            .OnlyContain(d => !d.Has("spec.replicas"), "autoscaled deployments must not pin replicas");
    }

    [HelmFact]
    public void infrastructure_components_reuse_the_compose_hostnames_credentials_and_init_sql()
    {
        var objects = HelmChart.Render(Kind);

        var services = objects.Where(o => o.Kind == "Service").ToDictionary(o => o.Name, StringComparer.Ordinal);
        services["postgres"].Get("spec.ports[postgres].port").Should().Be("5432");
        services["mongo"].Get("spec.ports[mongo].port").Should().Be("27017");
        services["rabbitmq"].Get("spec.ports[amqp].port").Should().Be("5672");
        services["rabbitmq"].Get("spec.ports[management].port").Should().Be("15672");
        services["eventstore"].Get("spec.ports[http].port").Should().Be("2113");

        var infraSecret = objects
            .Single(o => o.Kind == "Secret" && o.Name == "booking-infrastructure")
            .Map("stringData");
        infraSecret
            .Should()
            .Contain("POSTGRES_PASSWORD", "postgres")
            .And.Contain("RABBITMQ_DEFAULT_USER", "guest")
            .And.Contain("RABBITMQ_DEFAULT_PASS", "guest");

        var initSql = objects.Single(o => o.Kind == "ConfigMap" && o.Name == "postgres-init").Map("data");
        initSql.Should().ContainSingle().Which.Key.Should().Be("01-service-databases.sql");
        initSql["01-service-databases.sql"]
            .Trim()
            .Should()
            .Be(File.ReadAllText(Path.Combine(HelmChart.ChartDir, "files", "postgres-init.sql")).Trim());

        var postgres = objects.Single(o => o.Kind == "Deployment" && o.Name == "postgres");
        postgres.Get("spec.template.spec.containers.0.image").Should().Be("postgres:16");
        postgres.Get("spec.template.spec.enableServiceLinks").Should().Be("false");
        postgres
            .List("spec.template.spec.containers.0.volumeMounts")
            .Should()
            .Contain(m => K8sObject.Scalar(m, "mountPath") == "/docker-entrypoint-initdb.d");
        postgres.Has("spec.template.metadata.annotations.checksum/init").Should().BeTrue();

        var eventstore = objects.Single(o => o.Kind == "Deployment" && o.Name == "eventstore");
        eventstore
            .Get("spec.template.spec.enableServiceLinks")
            .Should()
            .Be("false", "EVENTSTORE_SERVICE_PORT from service links breaks EventStoreDB");
        eventstore
            .List("spec.template.spec.containers.0.env")
            .Should()
            .Contain(e =>
                K8sObject.Scalar(e, "name") == "EVENTSTORE_INSECURE" && K8sObject.Scalar(e, "value") == "true"
            );
    }

    [HelmFact]
    public void infrastructure_persistence_renders_a_claim_and_mounts_it()
    {
        var objects = HelmChart.Render(
            Kind.Concat(
                    [
                        "--set",
                        "infrastructure.postgres.persistence.enabled=true",
                        "--set",
                        "infrastructure.postgres.persistence.storageClass=fast",
                    ]
                )
                .ToArray()
        );

        var pvc = objects.Should().ContainSingle(o => o.Kind == "PersistentVolumeClaim").Subject;
        pvc.Name.Should().Be("postgres-data");
        pvc.Get("spec.storageClassName").Should().Be("fast");
        pvc.Get("spec.resources.requests.storage").Should().Be("2Gi");

        var postgres = objects.Single(o => o.Kind == "Deployment" && o.Name == "postgres");
        postgres.List("spec.template.spec.volumes").Should().Contain(v => K8sObject.Scalar(v, "name") == "data");
        postgres.Get("spec.template.spec.volumes[data].persistentVolumeClaim.claimName").Should().Be("postgres-data");
        postgres
            .Get("spec.template.spec.containers.0.volumeMounts[data].mountPath")
            .Should()
            .Be("/var/lib/postgresql/data");
    }

    [HelmFact]
    public void defaults_env_and_secret_env_reach_every_service_configmap_and_secret()
    {
        var objects = HelmChart.Render(
            "--set",
            "defaults.env.FEATURE_FLAG=on",
            "--set",
            "defaults.secretEnv.SHARED_TOKEN=dev-only"
        );

        foreach (var name in new[] { "gateway", "identity-service", "flight-service", "passenger-service", "booking-service" })
        {
            objects
                .Single(o => o.Kind == "ConfigMap" && o.Name == $"{name}-config")
                .Map("data")
                .Should()
                .ContainKey("FEATURE_FLAG");
            objects
                .Single(o => o.Kind == "Secret" && o.Name == $"{name}-secret")
                .Map("stringData")
                .Should()
                .ContainKey("SHARED_TOKEN");
        }
    }
}
