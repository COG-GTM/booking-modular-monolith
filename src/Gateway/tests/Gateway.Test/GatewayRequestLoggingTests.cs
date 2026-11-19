using System.Collections.Concurrent;
using System.Diagnostics;
using Gateway.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Gateway.Test;

public class GatewayRequestLoggingTests
{
    [Fact]
    public async Task proxied_request_logs_route_cluster_destination_and_request_identifiers()
    {
        await using var backend = await EchoBackend.StartAsync("monolith");
        using var capture = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        await using var factory = GatewayFactory.AllClustersTo(backend);
        await using var loggedFactory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton(loggerFactory.CreateLogger<GatewayRequestLoggingMiddleware>())
            )
        );
        using var client = loggedFactory.CreateClient();
        const string traceId = "0af7651916cd43dd8448eb211c80319c";
        var correlationId = Guid.NewGuid().ToString("N");
        using var request = new HttpRequestMessage(HttpMethod.Get, "/connect/token");
        request.Headers.Add("correlationId", correlationId);
        request.Headers.Add("traceparent", $"00-{traceId}-b7ad6b7169203331-01");

        using var response = await client.SendAsync(request);

        var entry = Assert.Single(capture.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("GET", entry.Fields["Method"]);
        Assert.Equal("/connect/token", entry.Fields["Path"]);
        Assert.Equal((int)response.StatusCode, entry.Fields["StatusCode"]);
        Assert.Equal(200, entry.Fields["StatusCode"]);
        Assert.Equal("identity-connect", entry.Fields["RouteId"]);
        Assert.Equal("identity", entry.Fields["ClusterId"]);
        Assert.Equal(backend.Address, entry.Fields["Destination"]);
        Assert.Equal(correlationId, entry.Fields["CorrelationId"]);
        Assert.Equal(traceId, entry.Fields["TraceId"]);
        Assert.True(Assert.IsType<double>(entry.Fields["ElapsedMs"]) >= 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task logs_without_proxy_metadata_even_when_downstream_throws(bool throws)
    {
        using var capture = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(capture));
        using var activity = new Activity("logging-test").Start();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/health";
        context.Response.StatusCode = throws ? 503 : 204;
        var error = new InvalidOperationException("downstream failure");
        var middleware = new GatewayRequestLoggingMiddleware(
            _ => throws ? Task.FromException(error) : Task.CompletedTask,
            loggerFactory.CreateLogger<GatewayRequestLoggingMiddleware>()
        );

        if (throws)
            Assert.Same(
                error,
                await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context))
            );
        else
            await middleware.InvokeAsync(context);

        var fields = Assert.Single(capture.Entries).Fields;
        Assert.Equal(context.Response.StatusCode, fields["StatusCode"]);
        Assert.Equal("-", fields["RouteId"]);
        Assert.Equal("-", fields["ClusterId"]);
        Assert.Equal("-", fields["Destination"]);
        Assert.Equal("-", fields["CorrelationId"]);
        Assert.Equal(activity.TraceId.ToString(), fields["TraceId"]);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, Dictionary<string, object?> Fields)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter
            ) =>
                provider.Entries.Enqueue(
                    (logLevel, ((IEnumerable<KeyValuePair<string, object?>>)state!).ToDictionary())
                );
        }
    }
}
