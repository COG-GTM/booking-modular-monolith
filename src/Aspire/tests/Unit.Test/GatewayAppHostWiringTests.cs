using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppHost.Unit.Test;

public class GatewayAppHostWiringTests
{
    private const string IdentityUrl = "{identity.bindings.http.url}";
    private const string ApiHttpUrl = "{api.bindings.api-http.url}";
    private const string ApiHttpsUrl = "{api.bindings.api-https.url}";
    private static readonly string[] Clusters = ["flight", "passenger", "booking", "identity", "monolith"];
    private static readonly string[] Services = ["identity", "flight", "passenger", "booking"];

    private static readonly Dictionary<string, string> ServiceDestinations = new()
    {
        ["flight"] = "{flight.bindings.http.url}",
        ["passenger"] = "{passenger.bindings.Http.url}",
        ["booking"] = "{booking.bindings.http.url}",
        ["identity"] = IdentityUrl,
        ["monolith"] = IdentityUrl,
    };

    private static readonly Dictionary<string, string?> MonolithTopology = new() { ["AppHost:Topology"] = "Monolith" };

    [Fact]
    public async Task default_topology_runs_the_gateway_and_four_services_without_the_monolith()
    {
        await using var appHost = await BuildAppHostAsync();
        var model = appHost.App.Services.GetRequiredService<DistributedApplicationModel>();
        var projects = model.Resources.OfType<ProjectResource>().Select(project => project.Name).Order();

        Assert.Equal(["booking", "flight", "gateway", "identity", "passenger"], projects);
    }

    [Fact]
    public async Task every_gateway_cluster_defaults_to_its_standalone_service()
    {
        await using var appHost = await BuildAppHostAsync();
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        foreach (var cluster in Clusters)
        {
            Assert.Equal(ServiceDestinations[cluster], env[ClusterAddressKey(cluster)]);
        }
    }

    [Theory]
    [InlineData("flight")]
    [InlineData("passenger")]
    [InlineData("booking")]
    [InlineData("identity")]
    [InlineData("monolith")]
    public async Task a_cluster_is_repointed_only_through_its_gateway_clusters_override(string cluster)
    {
        var overrideAddress = $"http://{cluster}-service:80";
        await using var appHost = await BuildAppHostAsync(new() { [$"Gateway:Clusters:{cluster}"] = overrideAddress });
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(overrideAddress, env[ClusterAddressKey(cluster)]);

        foreach (var other in Clusters.Where(name => name != cluster))
        {
            Assert.Equal(ServiceDestinations[other], env[ClusterAddressKey(other)]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task blank_cluster_override_keeps_the_default_destination(string blank)
    {
        await using var appHost = await BuildAppHostAsync(new() { ["Gateway:Clusters:flight"] = blank });
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(ServiceDestinations["flight"], env[ClusterAddressKey("flight")]);
    }

    [Fact]
    public async Task every_service_validates_tokens_against_the_identity_issuer()
    {
        await using var appHost = await BuildAppHostAsync();
        var identityEnv = await GetEnvironmentAsync(appHost.App, "identity");

        Assert.Equal(IdentityUrl, identityEnv["AuthOptions__IssuerUri"]);

        foreach (var name in Services.Append("gateway"))
        {
            var env = await GetEnvironmentAsync(appHost.App, name);
            Assert.Equal(IdentityUrl, env["Jwt__Authority"]);
        }
    }

    [Theory]
    [InlineData("identity", new[] { "identity", "rabbitmq" }, new[] { "flight", "passenger", "booking", "eventstore" })]
    [InlineData("flight", new[] { "flight", "flight-read", "rabbitmq" }, new[] { "identity", "passenger", "booking", "eventstore" })]
    [InlineData("passenger", new[] { "passenger", "passenger-read", "rabbitmq" }, new[] { "identity", "flight", "booking", "eventstore" })]
    [InlineData("booking", new[] { "booking", "booking-read", "eventstore", "rabbitmq" }, new[] { "identity", "flight", "passenger" })]
    public async Task each_service_only_gets_its_own_connection_strings(string service, string[] owned, string[] foreign)
    {
        await using var appHost = await BuildAppHostAsync();
        var env = await GetEnvironmentAsync(appHost.App, service);

        foreach (var name in owned)
        {
            Assert.True(env.ContainsKey($"ConnectionStrings__{name}"), $"{service} is missing ConnectionStrings__{name}");
        }

        foreach (var name in foreign)
        {
            Assert.False(env.ContainsKey($"ConnectionStrings__{name}"), $"{service} must not get ConnectionStrings__{name}");
        }
    }

    [Fact]
    public async Task booking_discovers_flight_and_passenger_grpc_endpoints_by_name()
    {
        await using var appHost = await BuildAppHostAsync();
        var env = await GetEnvironmentAsync(appHost.App, "booking");

        Assert.Equal("https://flight", env["Grpc__Flight__Address"]);
        Assert.Equal("http://_grpc.passenger", env["Grpc__Passenger__Address"]);
        Assert.Equal("{flight.bindings.https.url}", env["services__flight__https__0"]);
        Assert.Equal("{passenger.bindings.Grpc.url}", env["services__passenger__Grpc__0"]);
    }

    [Fact]
    public async Task gateway_is_exposed_on_its_own_ports_and_waits_for_every_service()
    {
        await using var appHost = await BuildAppHostAsync();
        var gateway = GetProject(appHost.App, "gateway");

        var endpoints = gateway.Annotations.OfType<EndpointAnnotation>().ToDictionary(endpoint => endpoint.Name);
        Assert.Equal(5000, endpoints["gateway-http"].Port);
        Assert.Equal("http", endpoints["gateway-http"].UriScheme);
        Assert.Equal(5001, endpoints["gateway-https"].Port);
        Assert.Equal("https", endpoints["gateway-https"].UriScheme);

        var waits = gateway.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name).ToHashSet();
        Assert.Superset(Services.ToHashSet(), waits);
    }

    [Fact]
    public async Task monolith_topology_runs_only_the_api_behind_the_gateway()
    {
        await using var appHost = await BuildAppHostAsync(MonolithTopology);
        var model = appHost.App.Services.GetRequiredService<DistributedApplicationModel>();
        var projects = model.Resources.OfType<ProjectResource>().Select(project => project.Name).Order();
        var gatewayEnv = await GetEnvironmentAsync(appHost.App, "gateway");
        var apiEnv = await GetEnvironmentAsync(appHost.App, "api");

        Assert.Equal(["api", "gateway"], projects);

        foreach (var cluster in Clusters)
        {
            Assert.Equal(ApiHttpUrl, gatewayEnv[ClusterAddressKey(cluster)]);
        }

        Assert.Equal(ApiHttpsUrl, apiEnv["AuthOptions__IssuerUri"]);
        Assert.Equal(ApiHttpsUrl, apiEnv["Jwt__Authority"]);
        Assert.Equal(ApiHttpsUrl, gatewayEnv["Jwt__Authority"]);
    }

    private static string ClusterAddressKey(string cluster) =>
        $"ReverseProxy__Clusters__{cluster}__Destinations__monolith__Address";

    private static async Task<AppHostUnderTest> BuildAppHostAsync(Dictionary<string, string?>? configuration = null)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            [],
            (_, settings) => settings.Configuration?.AddInMemoryCollection(configuration ?? [])
        );

        return await AppHostUnderTest.CreateAsync(builder);
    }

    /// <summary>
    /// Builds the AppHost and parks its entry point at <see cref="BeforeStartEvent"/>, so the resource model
    /// can be inspected without Aspire's start-up hooks mutating annotations concurrently and without the
    /// orchestrator ever starting.
    /// </summary>
    private sealed class AppHostUnderTest : IAsyncDisposable
    {
        private readonly TaskCompletionSource _resume = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private AppHostUnderTest(DistributedApplication app) => App = app;

        public DistributedApplication App { get; }

        public static async Task<AppHostUnderTest> CreateAsync(IDistributedApplicationTestingBuilder builder)
        {
            var reachedBeforeStart = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            AppHostUnderTest? appHost = null;

            builder.Eventing.Subscribe<BeforeStartEvent>(
                async (_, _) =>
                {
                    reachedBeforeStart.TrySetResult();
                    await appHost!._resume.Task;
                }
            );

            appHost = new AppHostUnderTest(await builder.BuildAsync());
            await reachedBeforeStart.Task.WaitAsync(TimeSpan.FromSeconds(30));

            return appHost;
        }

        public async ValueTask DisposeAsync()
        {
            _resume.TrySetResult();
            await App.DisposeAsync();
        }
    }

    private static ProjectResource GetProject(DistributedApplication app, string name)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        return Assert.Single(model.Resources.OfType<ProjectResource>(), resource => resource.Name == name);
    }

    private static async Task<Dictionary<string, string>> GetEnvironmentAsync(DistributedApplication app, string name)
    {
        var configuration = await ExecutionConfigurationBuilder
            .Create(GetProject(app, name))
            .WithEnvironmentVariablesConfig()
            .BuildAsync(
                new DistributedApplicationExecutionContext(
                    new DistributedApplicationExecutionContextOptions(DistributedApplicationOperation.Publish)
                    {
                        ServiceProvider = app.Services,
                    }
                )
            );

        Assert.Null(configuration.Exception);

        return new Dictionary<string, string>(configuration.EnvironmentVariables);
    }
}
