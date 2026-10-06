using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace AppHost.Test;

/// <summary>
/// Builds the AppHost model and freezes it at <see cref="BeforeStartEvent"/>, before Aspire's orchestrator,
/// publisher or DCP mutate resource annotations in the background. Disposing releases the gate.
/// </summary>
internal sealed class FrozenAppHost : IAsyncDisposable
{
    private readonly DistributedApplication _app;
    private readonly TaskCompletionSource _gate;
    private readonly DistributedApplicationExecutionContext _executionContext;

    private FrozenAppHost(DistributedApplication app, TaskCompletionSource gate, bool publish)
    {
        _app = app;
        _gate = gate;
        _executionContext = new DistributedApplicationExecutionContext(
            publish ? DistributedApplicationOperation.Publish : DistributedApplicationOperation.Run
        );
    }

    public IReadOnlyList<IResource> Resources =>
        _app.Services.GetRequiredService<DistributedApplicationModel>().Resources.ToList();

    public static async Task<FrozenAppHost> CreateAsync(bool publish)
    {
        var args = publish
            ? new[]
            {
                "--operation",
                "publish",
                "--publisher",
                "docker-compose",
                "--output-path",
                Path.GetTempPath(),
            }
            : [];
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>(args);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var frozen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        builder.Eventing.Subscribe<BeforeStartEvent>(
            async (_, cancellationToken) =>
            {
                frozen.TrySetResult();
                await gate.Task.WaitAsync(cancellationToken);
            }
        );

        var app = await builder.BuildAsync();
        await frozen.Task.WaitAsync(TimeSpan.FromSeconds(30));

        return new FrozenAppHost(app, gate, publish);
    }

    public IResource Resource(string name) => Resources.Single(resource => resource.Name == name);

    public ParameterResource Parameter(string name) =>
        Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == name);

    public async Task<Dictionary<string, object>> GetEnvironmentAsync(string resourceName)
    {
        var resource = Resource(resourceName);
        var context = new EnvironmentCallbackContext(_executionContext, resource);

        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>().ToList())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }

    public async ValueTask DisposeAsync()
    {
        _gate.TrySetCanceled();
        await _app.DisposeAsync();
    }
}
