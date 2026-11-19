using System.Diagnostics;
using Yarp.ReverseProxy.Model;

namespace Gateway.Middleware;

public partial class GatewayRequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GatewayRequestLoggingMiddleware> _logger;

    public GatewayRequestLoggingMiddleware(RequestDelegate next, ILogger<GatewayRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var startedAt = Stopwatch.GetTimestamp();

        try
        {
            await _next(context);
        }
        finally
        {
            var proxyFeature = context.Features.Get<IReverseProxyFeature>();

            LogRequest(
                _logger,
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                context.Response.StatusCode,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
                proxyFeature?.Route.Config.RouteId ?? "-",
                proxyFeature?.Route.Cluster?.ClusterId ?? "-",
                proxyFeature?.ProxiedDestination?.Model.Config.Address ?? "-",
                context.Items[CorrelationIdMiddleware.HeaderName] as string ?? "-",
                Activity.Current?.TraceId.ToString() ?? "-"
            );
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Gateway {Method} {Path} -> {StatusCode} in {ElapsedMs:0.0} ms (route={RouteId}, cluster={ClusterId}, destination={Destination}, correlationId={CorrelationId}, traceId={TraceId})"
    )]
    private static partial void LogRequest(
        ILogger logger,
        string method,
        string path,
        int statusCode,
        double elapsedMs,
        string routeId,
        string clusterId,
        string destination,
        string correlationId,
        string traceId
    );
}
