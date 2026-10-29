using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gateway.Test;

/// <summary>Real Kestrel backend that records what the gateway forwarded to it.</summary>
public sealed class EchoBackend : IAsyncDisposable
{
    private readonly WebApplication _app;

    private EchoBackend(string name, WebApplication app)
    {
        Name = name;
        _app = app;
    }

    public string Name { get; }
    public string Address { get; private set; } = string.Empty;
    public ConcurrentQueue<ForwardedRequest> Requests { get; } = new();

    public static async Task<EchoBackend> StartAsync(string name)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        var backend = new EchoBackend(name, app);

        app.Run(async context =>
        {
            backend.Requests.Enqueue(
                new ForwardedRequest(
                    context.Request.Method,
                    context.Request.Path.Value ?? "/",
                    context.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase)
                )
            );
            context.Response.Headers["X-Backend"] = name;
            await context.Response.WriteAsync(name);
        });

        await app.StartAsync();
        backend.Address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First() + "/";

        return backend;
    }

    public ValueTask DisposeAsync() => _app.DisposeAsync();
}

public sealed record ForwardedRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers);
