using FluentAssertions;
using Xunit;

namespace Helm.Test;

public class RenderGuardTests
{
    private static string[] Production =>
        ["-f", HelmChart.ValuesFile("values-production.yaml"), "--set", "global.image.tag=abc123"];

    [HelmFact]
    public void identity_with_more_than_one_replica_requires_a_shared_signing_key()
    {
        var error = HelmChart.RenderError("--set", "services.identity-service.replicaCount=2");

        error
            .Should()
            .Contain(
                "services.identity-service.signingKey.existingSecret is required when identity-service replicaCount > 1 or autoscaling.enabled"
            );
    }

    [HelmFact]
    public void autoscaled_identity_requires_a_shared_signing_key()
    {
        var error = HelmChart.RenderError("--set", "services.identity-service.autoscaling.enabled=true");

        error.Should().Contain("services.identity-service.signingKey.existingSecret is required");
    }

    [HelmFact]
    public void autoscaling_enabled_through_defaults_also_triggers_the_signing_key_guard()
    {
        var error = HelmChart.RenderError("--set", "defaults.autoscaling.enabled=true");

        error.Should().Contain("services.identity-service.signingKey.existingSecret is required");
    }

    [HelmFact]
    public void scaled_identity_renders_once_a_signing_key_secret_is_configured()
    {
        var objects = HelmChart.Render(
            "--set",
            "services.identity-service.replicaCount=2",
            "--set",
            "services.identity-service.signingKey.existingSecret=identity-signing-key"
        );

        objects
            .Single(o => o.Kind == "Deployment" && o.Name == "identity-service")
            .Get("spec.replicas")
            .Should()
            .Be("2");
    }

    [HelmFact]
    public void other_services_may_scale_without_a_signing_key()
    {
        var objects = HelmChart.Render(
            "--set",
            "services.flight-service.replicaCount=3",
            "--set",
            "services.gateway.autoscaling.enabled=true"
        );

        objects.Single(o => o.Kind == "Deployment" && o.Name == "flight-service").Get("spec.replicas").Should().Be("3");
        objects.Should().ContainSingle(o => o.Kind == "HorizontalPodAutoscaler").Which.Name.Should().Be("gateway");
    }

    [HelmFact]
    public void require_existing_secrets_fails_for_the_first_service_without_one()
    {
        var error = HelmChart.RenderError(
            Production.Concat(["--set", "services.flight-service.existingSecret="]).ToArray()
        );

        error
            .Should()
            .Contain("services.flight-service.existingSecret is required when global.requireExistingSecrets=true");
    }

    [HelmFact]
    public void require_existing_secrets_is_satisfied_when_every_enabled_service_has_one()
    {
        var objects = HelmChart.Render(
            "--set",
            "global.requireExistingSecrets=true",
            "--set",
            "services.gateway.existingSecret=g",
            "--set",
            "services.identity-service.existingSecret=i",
            "--set",
            "services.flight-service.existingSecret=f",
            "--set",
            "services.passenger-service.existingSecret=p",
            "--set",
            "services.booking-service.enabled=false"
        );

        objects.Should().NotContain(o => o.Kind == "Secret");
        objects.Where(o => o.Kind == "Deployment").Should().HaveCount(4);
    }

    [HelmFact]
    public void disabled_services_are_exempt_from_require_existing_secrets()
    {
        var error = HelmChart.RenderError(
            "--set",
            "global.requireExistingSecrets=true",
            "--set",
            "services.gateway.enabled=false",
            "--set",
            "services.identity-service.existingSecret=i",
            "--set",
            "services.flight-service.existingSecret=f",
            "--set",
            "services.passenger-service.existingSecret=p"
        );

        error
            .Should()
            .Contain("services.booking-service.existingSecret is required")
            .And.NotContain("services.gateway.existingSecret");
    }

    [HelmFact]
    public void ingress_must_point_at_an_enabled_service()
    {
        HelmChart
            .RenderError("--set", "ingress.service=not-a-service")
            .Should()
            .Contain("ingress.service \"not-a-service\" is not an enabled entry under services");

        HelmChart
            .RenderError("--set", "services.gateway.enabled=false")
            .Should()
            .Contain("ingress.service \"gateway\" is not an enabled entry under services");
    }

    [HelmFact]
    public void ingress_guard_is_skipped_when_the_ingress_is_disabled()
    {
        var objects = HelmChart.Render("--set", "ingress.enabled=false", "--set", "services.gateway.enabled=false");

        objects.Should().NotContain(o => o.Kind == "Ingress");
        objects.Should().NotContain(o => o.Name.StartsWith("gateway", StringComparison.Ordinal));
    }
}
