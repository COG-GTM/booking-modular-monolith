using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppHost.Unit.Test;

public class GatewayAppHostWiringTests
{
    private const string ApiHttpUrl = "{api.bindings.api-http.url}";
    private const string ApiHttpsUrl = "{api.bindings.api-https.url}";
    private static readonly string[] Clusters = ["flight", "passenger", "booking", "identity", "monolith"];

    [Fact]
    public async Task every_gateway_cluster_defaults_to_the_monolith_http_endpoint()
    {
        await using var appHost = await BuildAppHostAsync();
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        foreach (var cluster in Clusters)
        {
            Assert.Equal(ApiHttpUrl, env[ClusterAddressKey(cluster)]);
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
        var extractedService = $"http://{cluster}-service:80";
        await using var appHost = await BuildAppHostAsync(new() { [$"Gateway:Clusters:{cluster}"] = extractedService });
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(extractedService, env[ClusterAddressKey(cluster)]);

        foreach (var other in Clusters.Where(name => name != cluster))
        {
            Assert.Equal(ApiHttpUrl, env[ClusterAddressKey(other)]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task blank_cluster_override_keeps_the_monolith_destination(string blank)
    {
        await using var appHost = await BuildAppHostAsync(new() { ["Gateway:Clusters:flight"] = blank });
        var env = await GetEnvironmentAsync(appHost.App, "gateway");

        Assert.Equal(ApiHttpUrl, env[ClusterAddressKey("flight")]);
    }

    [Fact]
    public async Task gateway_and_api_validate_tokens_against_the_same_issuer_the_api_issues()
    {
        await using var appHost = await BuildAppHostAsync();
        var gatewayEnv = await GetEnvironmentAsync(appHost.App, "gateway");
        var apiEnv = await GetEnvironmentAsync(appHost.App, "api");

        Assert.Equal(ApiHttpsUrl, apiEnv["AuthOptions__IssuerUri"]);
        Assert.Equal(ApiHttpsUrl, apiEnv["Jwt__Authority"]);
        Assert.Equal(ApiHttpsUrl, gatewayEnv["Jwt__Authority"]);
    }

    [Fact]
    public async Task gateway_is_exposed_on_its_own_ports_and_waits_for_the_api()
    {
        await using var appHost = await BuildAppHostAsync();
        var gateway = GetProject(appHost.App, "gateway");

        var endpoints = gateway.Annotations.OfType<EndpointAnnotation>().ToDictionary(endpoint => endpoint.Name);
        Assert.Equal(5000, endpoints["gateway-http"].Port);
        Assert.Equal("http", endpoints["gateway-http"].UriScheme);
        Assert.Equal(5001, endpoints["gateway-https"].Port);
        Assert.Equal("https", endpoints["gateway-https"].UriScheme);

        Assert.Contains(gateway.Annotations.OfType<WaitAnnotation>(), wait => wait.Resource.Name == "api");
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
