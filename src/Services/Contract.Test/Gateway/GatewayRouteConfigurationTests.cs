using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Contract.Test.Gateway;

/// <summary>
/// The YARP gateway is the single public ingress. Every service must stay reachable through it under the same
/// <c>/api/{version}/&lt;module&gt;/...</c> shape the monolith exposed, and Identity's OpenID Connect endpoints must be
/// proxied so tokens issued via the gateway validate against the same authority the services trust.
/// </summary>
public class GatewayRouteConfigurationTests
{
    private static readonly string[] Services = ["flight", "passenger", "booking", "identity"];

    private static IConfigurationSection ReverseProxy(string? environment = null)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(AppContext.BaseDirectory, "Gateway"))
            .AddJsonFile("appsettings.json", optional: false);

        if (environment is not null)
            builder.AddJsonFile($"appsettings.{environment}.json", optional: false);

        return builder.Build().GetSection("ReverseProxy");
    }

    private static Dictionary<string, IConfigurationSection> Children(IConfigurationSection section) =>
        section.GetChildren().ToDictionary(c => c.Key, c => c);

    [Theory]
    [MemberData(nameof(ServicesData))]
    public void each_service_api_is_routed_under_its_monolith_path_prefix(string service)
    {
        var routes = Children(ReverseProxy().GetSection("Routes"));

        var route = routes.Should().ContainKey($"{service}-api").WhoseValue;
        route["ClusterId"].Should().Be(service);
        route["Match:Path"].Should().Be($"/api/{{version}}/{service}/{{**catch-all}}");
    }

    public static TheoryData<string> ServicesData => new(Services);

    [Fact]
    public void identity_server_endpoints_are_proxied_to_the_identity_cluster()
    {
        var routes = Children(ReverseProxy().GetSection("Routes"));

        routes.Should().ContainKey("identity-connect").WhoseValue["Match:Path"].Should().Be("/connect/{**catch-all}");
        routes["identity-connect"]["ClusterId"].Should().Be("identity");
        routes
            .Should()
            .ContainKey("identity-discovery")
            .WhoseValue["Match:Path"]
            .Should()
            .Be("/.well-known/{**catch-all}");
        routes["identity-discovery"]["ClusterId"].Should().Be("identity");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Development")]
    [InlineData("docker")]
    public void every_route_targets_a_cluster_with_at_least_one_destination(string? environment)
    {
        var reverseProxy = ReverseProxy(environment);
        var routes = Children(reverseProxy.GetSection("Routes"));
        var clusters = Children(reverseProxy.GetSection("Clusters"));

        clusters.Keys.Should().BeEquivalentTo(Services);

        foreach (var (name, route) in routes)
        {
            var cluster = clusters
                .Should()
                .ContainKey(route["ClusterId"]!, $"route '{name}' must target a known cluster")
                .WhoseValue;
            cluster
                .GetSection("Destinations")
                .GetChildren()
                .Select(d => d["Address"])
                .Should()
                .NotBeEmpty()
                .And.AllSatisfy(address => Uri.IsWellFormedUriString(address, UriKind.Absolute).Should().BeTrue());
        }
    }

    [Fact]
    public void only_expected_route_names_are_configured()
    {
        Children(ReverseProxy().GetSection("Routes"))
            .Keys.Should()
            .BeEquivalentTo(Services.Select(s => $"{s}-api").Append("identity-connect").Append("identity-discovery"));
    }
}
