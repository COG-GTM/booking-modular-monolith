using FluentAssertions;
using Xunit;

namespace Helm.Test;

public class ChartTopologyTests
{
    private static readonly string[] ServiceNames =
    [
        "gateway",
        "identity-service",
        "flight-service",
        "passenger-service",
        "booking-service",
    ];
    private static readonly string[] InfrastructureNames = ["postgres", "mongo", "rabbitmq", "eventstore"];

    private static string[] Kind => ["-f", HelmChart.ValuesFile("values-kind.yaml")];

    private static string[] Production =>
        ["-f", HelmChart.ValuesFile("values-production.yaml"), "--set", "global.image.tag=abc123"];

    [HelmFact]
    public void default_values_render_one_deployment_service_configmap_and_secret_per_service_and_no_hpa_or_infrastructure()
    {
        var objects = HelmChart.Render();

        Names(objects, "Deployment").Should().BeEquivalentTo(ServiceNames);
        Names(objects, "Service").Should().BeEquivalentTo(ServiceNames);
        Names(objects, "ConfigMap").Should().BeEquivalentTo(ServiceNames.Select(n => $"{n}-config"));
        Names(objects, "Secret").Should().BeEquivalentTo(ServiceNames.Select(n => $"{n}-secret"));
        Names(objects, "HorizontalPodAutoscaler").Should().BeEmpty();
        Names(objects, "Ingress").Should().Equal("gateway");
        objects.Select(o => o.Name).Should().NotContain(InfrastructureNames);
        objects.Select(o => o.Kind).Should().NotContain("PersistentVolumeClaim");
    }

    [HelmFact]
    public void kind_overlay_adds_in_cluster_infrastructure_and_an_hpa_per_service()
    {
        var objects = HelmChart.Render(Kind);

        Names(objects, "Deployment").Should().BeEquivalentTo(ServiceNames.Concat(InfrastructureNames));
        Names(objects, "Service").Should().BeEquivalentTo(ServiceNames.Concat(InfrastructureNames));
        Names(objects, "HorizontalPodAutoscaler").Should().BeEquivalentTo(ServiceNames);
        Names(objects, "ConfigMap")
            .Should()
            .BeEquivalentTo(ServiceNames.Select(n => $"{n}-config").Append("postgres-init"));
        Names(objects, "Secret")
            .Should()
            .BeEquivalentTo(ServiceNames.Select(n => $"{n}-secret").Append("booking-infrastructure"));
        Names(objects, "Ingress").Should().Equal("gateway");
        objects.Count(o => o.Kind == "PersistentVolumeClaim").Should().Be(0, "persistence is disabled by default");
    }

    [HelmFact]
    public void production_overlay_renders_no_secrets_and_no_infrastructure()
    {
        var objects = HelmChart.Render(Production);

        Names(objects, "Deployment").Should().BeEquivalentTo(ServiceNames);
        Names(objects, "Service").Should().BeEquivalentTo(ServiceNames);
        Names(objects, "ConfigMap").Should().BeEquivalentTo(ServiceNames.Select(n => $"{n}-config"));
        Names(objects, "HorizontalPodAutoscaler").Should().BeEquivalentTo(ServiceNames);
        Names(objects, "Secret").Should().BeEmpty("every service uses existingSecret");
        Names(objects, "Ingress").Should().Equal("gateway");
        objects.Select(o => o.Name).Should().NotContain(InfrastructureNames);
    }

    [HelmFact]
    public void services_keep_the_docker_compose_hostnames_and_ports()
    {
        var services = HelmChart
            .Render()
            .Where(o => o.Kind == "Service")
            .ToDictionary(o => o.Name, StringComparer.Ordinal);

        foreach (var svc in services.Values)
        {
            svc.Get("spec.type").Should().Be("ClusterIP", $"{svc} must stay internal");
            svc.Map("spec.selector")
                .Should()
                .Contain("app.kubernetes.io/name", svc.Name)
                .And.Contain("app.kubernetes.io/instance", "booking");
        }

        services["gateway"].Get("spec.ports[http].port").Should().Be("80");
        services["identity-service"].Get("spec.ports[http].port").Should().Be("80");
        services["booking-service"].Get("spec.ports[http].port").Should().Be("80");

        services["flight-service"].Get("spec.ports[http].port").Should().Be("80");
        services["flight-service"].Get("spec.ports[grpc].port").Should().Be("81");
        services["flight-service"].Get("spec.ports[grpc].appProtocol").Should().Be("kubernetes.io/h2c");
        services["flight-service"].Has("spec.ports[http].appProtocol").Should().BeFalse();

        services["passenger-service"].Get("spec.ports[http].port").Should().Be("5102");
        services["passenger-service"].Get("spec.ports[grpc].port").Should().Be("5202");
        services["passenger-service"].Get("spec.ports[grpc].appProtocol").Should().Be("kubernetes.io/h2c");

        foreach (var svc in services.Values)
        {
            foreach (var port in svc.List("spec.ports"))
            {
                K8sObject
                    .Scalar(port, "targetPort")
                    .Should()
                    .Be(K8sObject.Scalar(port, "name"), "ports target the named container port");
            }
        }
    }

    [HelmFact]
    public void service_ports_match_the_container_ports_of_the_deployment()
    {
        var objects = HelmChart.Render();
        var deployments = objects.Where(o => o.Kind == "Deployment").ToDictionary(o => o.Name, StringComparer.Ordinal);

        foreach (var svc in objects.Where(o => o.Kind == "Service"))
        {
            var containerPorts = deployments[svc.Name]
                .List("spec.template.spec.containers.0.ports")
                .ToDictionary(
                    p => K8sObject.Scalar(p, "name")!,
                    p => K8sObject.Scalar(p, "containerPort")!,
                    StringComparer.Ordinal
                );

            foreach (var port in svc.List("spec.ports"))
            {
                containerPorts
                    .Should()
                    .Contain(
                        K8sObject.Scalar(port, "name")!,
                        K8sObject.Scalar(port, "port")!,
                        $"{svc} port names/numbers must match the container"
                    );
            }
        }
    }

    [HelmFact]
    public void only_the_gateway_is_exposed_through_the_ingress()
    {
        var ingress = HelmChart.Render().Single(o => o.Kind == "Ingress");

        ingress.ApiVersion.Should().Be("networking.k8s.io/v1");
        ingress.Get("spec.ingressClassName").Should().Be("nginx");
        ingress.Has("spec.rules.0.host").Should().BeFalse("empty host matches any host");
        ingress.Has("spec.tls").Should().BeFalse();
        ingress.Get("spec.rules.0.http.paths.0.path").Should().Be("/");
        ingress.Get("spec.rules.0.http.paths.0.pathType").Should().Be("Prefix");
        ingress.Get("spec.rules.0.http.paths.0.backend.service.name").Should().Be("gateway");
        ingress.Get("spec.rules.0.http.paths.0.backend.service.port.name").Should().Be("http");
    }

    [HelmFact]
    public void production_ingress_terminates_tls_for_the_public_host()
    {
        var ingress = HelmChart.Render(Production).Single(o => o.Kind == "Ingress");

        ingress.Get("spec.rules.0.host").Should().Be("booking.example.com");
        ingress.Get("spec.tls.0.secretName").Should().Be("booking-gateway-tls");
        ingress.Get("spec.tls.0.hosts.0").Should().Be("booking.example.com");
        ingress
            .Map("metadata.annotations")
            .Should()
            .Contain("cert-manager.io/cluster-issuer", "letsencrypt")
            .And.Contain("nginx.ingress.kubernetes.io/ssl-redirect", "true");
    }

    [HelmFact]
    public void ingress_can_be_disabled()
    {
        HelmChart.Render("--set", "ingress.enabled=false").Should().NotContain(o => o.Kind == "Ingress");
    }

    [HelmFact]
    public void disabling_a_service_removes_all_of_its_objects()
    {
        var objects = HelmChart.Render(Kind.Concat(["--set", "services.booking-service.enabled=false"]).ToArray());

        objects.Should().NotContain(o => o.Name.StartsWith("booking-service", StringComparison.Ordinal));
        Names(objects, "Deployment").Should().Contain(ServiceNames.Where(n => n != "booking-service"));
    }

    [HelmFact]
    public void every_object_carries_the_common_and_selector_labels()
    {
        foreach (var obj in HelmChart.Render(Kind))
        {
            var labels = obj.Map("metadata.labels");
            labels
                .Should()
                .Contain("app.kubernetes.io/part-of", "booking", $"{obj}")
                .And.Contain("app.kubernetes.io/instance", "booking", $"{obj}")
                .And.Contain("app.kubernetes.io/managed-by", "Helm", $"{obj}")
                .And.ContainKey("app.kubernetes.io/name", $"{obj}")
                .And.ContainKey("app.kubernetes.io/component", $"{obj}")
                .And.ContainKey("helm.sh/chart", $"{obj}");
        }
    }

    [HelmFact]
    public void deployments_select_exactly_their_own_pods()
    {
        foreach (var deployment in HelmChart.Render(Kind).Where(o => o.Kind == "Deployment"))
        {
            var selector = deployment.Map("spec.selector.matchLabels");
            var podLabels = deployment.Map("spec.template.metadata.labels");

            selector
                .Should()
                .Equal(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["app.kubernetes.io/name"] = deployment.Name,
                        ["app.kubernetes.io/instance"] = "booking",
                    }
                );
            podLabels.Should().Contain(selector);
        }
    }

    private static List<string> Names(IEnumerable<K8sObject> objects, string kind) =>
        objects.Where(o => o.Kind == kind).Select(o => o.Name).ToList();
}
