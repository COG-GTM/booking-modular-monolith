using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace Helm.Test;

/// <summary>Consistency between the chart, the kind cluster config, the kind-up script and docker-compose.</summary>
public partial class KindToolingTests
{
    private static readonly string KindConfig = Path.Combine(HelmChart.KubernetesDir, "kind-config.yaml");
    private static readonly string KindUp = Path.Combine(HelmChart.KubernetesDir, "kind-up.sh");
    private static readonly string ComposeServices = Path.Combine(
        HelmChart.RepoRoot,
        "deployments",
        "docker-compose",
        "docker-compose.services.yaml"
    );

    [Fact]
    public void kind_pod_subnet_matches_the_trusted_proxy_network_of_the_kind_overlay()
    {
        var kind = HelmChart.LoadYaml(KindConfig);
        var values = HelmChart.LoadYaml(HelmChart.ValuesFile("values-kind.yaml"));

        var podSubnet = Scalar(kind, "networking", "podSubnet");
        var trusted = Scalar(values, "global", "env", "TrustedProxyOptions__KnownNetworks__0");

        podSubnet.Should().Be("10.244.0.0/16");
        trusted.Should().Be(podSubnet, "the gateway must trust X-Forwarded-For from ingress-nginx pods");
    }

    [Fact]
    public void kind_host_port_mapping_matches_the_public_url_of_the_kind_overlay()
    {
        var kind = HelmChart.LoadYaml(KindConfig);
        var values = HelmChart.LoadYaml(HelmChart.ValuesFile("values-kind.yaml"));

        var publicUrl = new Uri(Scalar(values, "global", "publicUrl")!);
        var controlPlane = ((YamlSequenceNode)kind.Children[new YamlScalarNode("nodes")])
            .Children.OfType<YamlMappingNode>()
            .Single();
        var mappings = ((YamlSequenceNode)controlPlane.Children[new YamlScalarNode("extraPortMappings")])
            .Children.OfType<YamlMappingNode>()
            .ToDictionary(
                m => K8sObject.Scalar(m, "containerPort")!,
                m => K8sObject.Scalar(m, "hostPort")!,
                StringComparer.Ordinal
            );

        mappings.Should().Contain("80", publicUrl.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        mappings.Should().Contain("443", "8443");
        K8sObject.Scalar(controlPlane, "role").Should().Be("control-plane");
        controlPlane.Children[new YamlScalarNode("kubeadmConfigPatches")].ToString().Should().NotBeNull();
        ((YamlSequenceNode)controlPlane.Children[new YamlScalarNode("kubeadmConfigPatches")])
            .Children.OfType<YamlScalarNode>()
            .Should()
            .ContainSingle(
                p => p.Value!.Contains("ingress-ready=true", StringComparison.Ordinal),
                "ingress-nginx's kind manifest schedules onto this label"
            );
    }

    [Fact]
    public void chart_init_sql_is_a_copy_of_the_compose_init_sql()
    {
        var chartSql = File.ReadAllText(Path.Combine(HelmChart.ChartDir, "files", "postgres-init.sql"));
        var composeSql = File.ReadAllText(
            Path.Combine(
                HelmChart.RepoRoot,
                "deployments",
                "docker-compose",
                "init",
                "postgres",
                "01-service-databases.sql"
            )
        );

        chartSql.Should().Be(composeSql);
    }

    [Fact]
    public void kind_up_builds_exactly_the_chart_services_from_existing_dockerfiles()
    {
        var script = File.ReadAllText(KindUp);
        var dockerfiles = DockerfileEntry()
            .Matches(script)
            .ToDictionary(m => m.Groups["svc"].Value, m => m.Groups["path"].Value, StringComparer.Ordinal);
        var chartServices = (
            (YamlMappingNode)
                HelmChart.LoadYaml(HelmChart.ValuesFile("values.yaml")).Children[new YamlScalarNode("services")]
        )
            .Children.Keys.Cast<YamlScalarNode>()
            .Select(k => k.Value!)
            .ToList();

        dockerfiles.Keys.Should().BeEquivalentTo(chartServices);
        foreach (var (svc, path) in dockerfiles)
        {
            File.Exists(Path.Combine(HelmChart.RepoRoot, path)).Should().BeTrue($"{svc} Dockerfile {path} must exist");
        }
    }

    [Fact]
    public void kind_up_installs_the_chart_with_the_kind_overlay_into_the_booking_namespace()
    {
        var script = File.ReadAllText(KindUp);

        script.Should().StartWith("#!/usr/bin/env bash").And.Contain("set -euo pipefail");
        script
            .Should()
            .Contain(
                "kind create cluster --name \"$CLUSTER\" --config \"$ROOT/deployments/kubernetes/kind-config.yaml\""
            );
        script.Should().Contain("kind load docker-image --name \"$CLUSTER\" \"booking/$svc:$TAG\"");
        script.Should().Contain("-f \"$ROOT/deployments/helm/booking/values-kind.yaml\"");
        script.Should().Contain("--namespace \"$NAMESPACE\" --create-namespace");
        script.Should().Contain("--set global.image.tag=\"$TAG\"");
        script.Should().Contain("NAMESPACE=\"${NAMESPACE:-booking}\"");
        script
            .Should()
            .Contain("kubectl -n \"$NAMESPACE\" create secret generic \"$SIGNING_SECRET\" --from-file=tempkey.jwk=");
        script.Should().Contain("SIGNING_SECRET=\"identity-signing-key\"");

        var values = HelmChart.LoadYaml(HelmChart.ValuesFile("values-kind.yaml"));
        Scalar(values, "services", "identity-service", "signingKey", "existingSecret")
            .Should()
            .Be("identity-signing-key", "the script's secret name must match the overlay");
    }

    [Fact]
    public void chart_services_mirror_the_compose_services_and_container_ports()
    {
        var compose = (YamlMappingNode)HelmChart.LoadYaml(ComposeServices).Children[new YamlScalarNode("services")];
        var chart = (YamlMappingNode)
            HelmChart.LoadYaml(HelmChart.ValuesFile("values.yaml")).Children[new YamlScalarNode("services")];

        var composeNames = compose.Children.Keys.Cast<YamlScalarNode>().Select(k => k.Value!).ToList();
        var chartNames = chart.Children.Keys.Cast<YamlScalarNode>().Select(k => k.Value!).ToList();
        chartNames.Should().BeEquivalentTo(composeNames, "in-cluster DNS must match the compose hostnames");

        foreach (var name in chartNames)
        {
            var composePorts = (
                (YamlSequenceNode)
                    ((YamlMappingNode)compose.Children[new YamlScalarNode(name)]).Children[new YamlScalarNode("ports")]
            )
                .Children.OfType<YamlScalarNode>()
                .Select(p => p.Value!.Split(':')[1])
                .ToList();
            var chartPorts = (
                (YamlSequenceNode)
                    ((YamlMappingNode)chart.Children[new YamlScalarNode(name)]).Children[new YamlScalarNode("ports")]
            )
                .Children.OfType<YamlMappingNode>()
                .Select(p => K8sObject.Scalar(p, "containerPort")!)
                .ToList();

            chartPorts
                .Should()
                .Contain(
                    composePorts,
                    $"every port compose publishes for {name} must be a container port in the chart"
                );
        }

        var flightPorts = (
            (YamlSequenceNode)
                ((YamlMappingNode)chart.Children[new YamlScalarNode("flight-service")]).Children[
                    new YamlScalarNode("ports")
                ]
        )
            .Children.OfType<YamlMappingNode>()
            .ToDictionary(
                p => K8sObject.Scalar(p, "name")!,
                p => K8sObject.Scalar(p, "containerPort")!,
                StringComparer.Ordinal
            );
        flightPorts
            .Should()
            .Contain(
                "grpc",
                "81",
                "booking-service dials flight-service:81 (not published by compose, but used on the compose network)"
            );
        var passengerPorts = (
            (YamlSequenceNode)
                ((YamlMappingNode)chart.Children[new YamlScalarNode("passenger-service")]).Children[
                    new YamlScalarNode("ports")
                ]
        )
            .Children.OfType<YamlMappingNode>()
            .ToDictionary(
                p => K8sObject.Scalar(p, "name")!,
                p => K8sObject.Scalar(p, "containerPort")!,
                StringComparer.Ordinal
            );
        passengerPorts.Should().Contain("grpc", "5202");
    }

    [Fact]
    public void shell_scripts_are_executable_and_parse()
    {
        foreach (var script in new[] { KindUp, Path.Combine(HelmChart.KubernetesDir, "smoke-test.sh") })
        {
            File.GetUnixFileMode(script).Should().HaveFlag(UnixFileMode.UserExecute, $"{Path.GetFileName(script)}");

            var psi = new System.Diagnostics.ProcessStartInfo("bash")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-n");
            psi.ArgumentList.Add(script);
            using var process = System.Diagnostics.Process.Start(psi)!;
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            process.ExitCode.Should().Be(0, stderr);
        }
    }

    private static string? Scalar(YamlMappingNode root, params string[] path)
    {
        YamlNode current = root;
        foreach (var key in path)
        {
            if (current is not YamlMappingNode map || !map.Children.TryGetValue(new YamlScalarNode(key), out current!))
            {
                return null;
            }
        }

        return (current as YamlScalarNode)?.Value;
    }

    [GeneratedRegex(@"\[(?<svc>[a-z-]+)\]=(?<path>\S+)")]
    private static partial Regex DockerfileEntry();
}
