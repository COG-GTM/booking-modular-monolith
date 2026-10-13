using Aspire.Hosting.ApplicationModel;
using Xunit;
using static AppHost.Unit.Test.AppHostModel;

namespace AppHost.Unit.Test;

// Nothing of the decommissioned src/Api host may survive in the resource graph (AB-505).
public class MonolithDecommissionTests
{
    private static readonly int[] MonolithPorts = [3000, 3001];

    [Fact]
    public async Task upper_case_monolith_topology_is_rejected_and_the_error_names_the_key_and_the_default()
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => BuildAsync(new() { ["AppHost:Topology"] = "MONOLITH" })
        );
        var message = error.ToString();

        Assert.Contains("AppHost:Topology", message, StringComparison.Ordinal);
        Assert.Contains("'MONOLITH'", message, StringComparison.Ordinal);
        Assert.Contains("'Services'", message, StringComparison.Ordinal);
        Assert.Contains("AB-505", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task no_project_is_named_api_or_listens_on_the_monolith_ports()
    {
        await using var appHost = await BuildAsync();
        var resources = GetResources(appHost.App).ToList();

        Assert.DoesNotContain(resources, resource => resource.Name.Equals("api", StringComparison.OrdinalIgnoreCase));

        // Only projects can replace the monolith host; containers (e.g. grafana on 3000) are unrelated.
        var monolithEndpoints = resources
            .OfType<ProjectResource>()
            .SelectMany(resource =>
                resource.Annotations.OfType<EndpointAnnotation>().Select(endpoint => (resource.Name, endpoint))
            )
            .Where(pair => pair.endpoint.Port is { } port && MonolithPorts.Contains(port))
            .Select(pair => $"{pair.Name}:{pair.endpoint.Name}={pair.endpoint.Port}")
            .ToList();

        Assert.Empty(monolithEndpoints);
    }

    [Fact]
    public async Task gateway_and_every_service_validate_tokens_against_identity()
    {
        await using var appHost = await BuildAsync();
        const string issuer = "{identity.bindings.http.url}";

        foreach (var name in new[] { "gateway", "identity", "flight", "passenger", "booking" })
        {
            var env = await GetEnvironmentAsync(appHost.App, name);
            Assert.Equal(issuer, env["Jwt__Authority"], ignoreCase: true);
        }

        var identityEnv = await GetEnvironmentAsync(appHost.App, "identity");
        Assert.Equal(issuer, identityEnv["AuthOptions__IssuerUri"], ignoreCase: true);
    }

    [Fact]
    public async Task catch_all_cluster_falls_back_to_identity_instead_of_a_monolith()
    {
        await using var appHost = await BuildAsync();
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(env[ClusterAddressKey("identity")], env[ClusterAddressKey("monolith")]);
    }
}
