using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppHost.Unit.Test;

internal static class AppHostModel
{
    public static readonly string[] Clusters = ["flight", "passenger", "booking", "identity", "monolith"];

    public static string ClusterAddressKey(string cluster) =>
        $"ReverseProxy__Clusters__{cluster}__Destinations__monolith__Address";

    public static async Task<AppHostUnderTest> BuildAsync(Dictionary<string, string?>? configuration = null)
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(
            [],
            (_, settings) => settings.Configuration?.AddInMemoryCollection(configuration ?? [])
        );

        return await AppHostUnderTest.CreateAsync(builder);
    }

    public static ProjectResource GetProject(DistributedApplication app, string name)
    {
        var model = app.Services.GetRequiredService<DistributedApplicationModel>();
        return Assert.Single(model.Resources.OfType<ProjectResource>(), resource => resource.Name == name);
    }

    public static IEnumerable<IResource> GetResources(DistributedApplication app) =>
        app.Services.GetRequiredService<DistributedApplicationModel>().Resources;

    public static async Task<Dictionary<string, string>> GetEnvironmentAsync(DistributedApplication app, string name)
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

/// <summary>
/// Builds the AppHost and parks its entry point at <see cref="BeforeStartEvent"/>, so the resource model
/// can be inspected without Aspire's start-up hooks mutating annotations concurrently and without the
/// orchestrator ever starting.
/// </summary>
internal sealed class AppHostUnderTest : IAsyncDisposable
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
