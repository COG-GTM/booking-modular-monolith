using System.Net;
using FluentAssertions;
using Xunit;

namespace Helm.Test;

/// <summary>ConfigMap, Secret and Service templates read the same defaults-merged service config as the Deployment.</summary>
public class ServiceDefaultsTests
{
    private static readonly string[] ServiceNames =
    [
        "gateway",
        "identity-service",
        "flight-service",
        "passenger-service",
        "booking-service",
    ];

    // values.yaml sets `existingSecret: ""` on every service, which overrides defaults.existingSecret; null it out.
    private static string[] InheritDefaultsExistingSecret(params string[] extra) =>
        ServiceNames
            .SelectMany(name => new[] { "--set", $"services.{name}.existingSecret=null" })
            .Concat(["--set", "defaults.existingSecret=shared-credentials"])
            .Concat(extra)
            .ToArray();

    [HelmFact]
    public void defaults_existing_secret_suppresses_chart_secrets_and_is_referenced_by_every_deployment()
    {
        var objects = HelmChart.Render(InheritDefaultsExistingSecret());

        objects.Where(o => o.Kind == "Secret").Should().BeEmpty("every service inherits defaults.existingSecret");
        foreach (var name in ServiceNames)
        {
            var deployment = objects.Single(o => o.Kind == "Deployment" && o.Name == name);
            deployment
                .Get("spec.template.spec.containers.0.envFrom.1.secretRef.name")
                .Should()
                .Be("shared-credentials", $"{deployment}");
            deployment.Has("spec.template.metadata.annotations.checksum/secret").Should().BeFalse($"{deployment}");
        }
    }

    [HelmFact]
    public void service_existing_secret_overrides_defaults_existing_secret()
    {
        var objects = HelmChart.Render(
            InheritDefaultsExistingSecret("--set", "services.flight-service.existingSecret=flight-credentials")
        );

        objects.Where(o => o.Kind == "Secret").Should().BeEmpty();
        objects
            .Single(o => o.Kind == "Deployment" && o.Name == "flight-service")
            .Get("spec.template.spec.containers.0.envFrom.1.secretRef.name")
            .Should()
            .Be("flight-credentials");
        objects
            .Single(o => o.Kind == "Deployment" && o.Name == "booking-service")
            .Get("spec.template.spec.containers.0.envFrom.1.secretRef.name")
            .Should()
            .Be("shared-credentials");
    }

    [HelmFact]
    public void defaults_existing_secret_satisfies_require_existing_secrets()
    {
        var objects = HelmChart.Render(InheritDefaultsExistingSecret("--set", "global.requireExistingSecrets=true"));

        objects.Where(o => o.Kind == "Secret").Should().BeEmpty();
        objects.Where(o => o.Kind == "Deployment").Should().HaveCount(5);
    }

    [HelmFact]
    public void service_env_wins_over_defaults_env_in_the_configmap()
    {
        var objects = HelmChart.Render(
            "--set",
            "defaults.env.FEATURE_FLAG=default",
            "--set",
            "services.booking-service.env.FEATURE_FLAG=booking"
        );

        objects
            .Single(o => o.Kind == "ConfigMap" && o.Name == "booking-service-config")
            .Map("data")
            .Should()
            .Contain("FEATURE_FLAG", "booking");
        objects
            .Single(o => o.Kind == "ConfigMap" && o.Name == "flight-service-config")
            .Map("data")
            .Should()
            .Contain("FEATURE_FLAG", "default");
    }

    [HelmFact]
    public void service_without_ports_exposes_the_defaults_ports_on_both_service_and_deployment()
    {
        var objects = HelmChart.Render(
            "--set",
            "services.flight-service.ports=null",
            "--set-json",
            """defaults.ports=[{"name":"http","containerPort":8080}]"""
        );

        var service = objects.Single(o => o.Kind == "Service" && o.Name == "flight-service");
        service.List("spec.ports").Should().ContainSingle();
        service.Get("spec.ports[http].port").Should().Be("8080");
        service.Get("spec.ports[http].targetPort").Should().Be("http");
        objects
            .Single(o => o.Kind == "Deployment" && o.Name == "flight-service")
            .Get("spec.template.spec.containers.0.ports[http].containerPort")
            .Should()
            .Be("8080");

        objects
            .Single(o => o.Kind == "Service" && o.Name == "passenger-service")
            .Get("spec.ports[grpc].port")
            .Should()
            .Be("5202", "services that declare ports keep their own");
    }

    [HelmFact]
    public void production_overlay_does_not_trust_a_cluster_wide_proxy_network()
    {
        var gateway = HelmChart
            .Render("-f", HelmChart.ValuesFile("values-production.yaml"), "--set", "global.image.tag=abc123")
            .Single(o => o.Kind == "ConfigMap" && o.Name == "gateway-config");

        var cidr = gateway.Map("data")["TrustedProxyOptions__KnownNetworks__0"];

        IPNetwork.TryParse(cidr, out var network).Should().BeTrue(cidr);
        network.PrefixLength.Should().BeGreaterThanOrEqualTo(16, "every address in the range may set X-Forwarded-For");
    }
}
