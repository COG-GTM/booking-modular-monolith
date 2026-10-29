using System.Text.Json;
using Gateway.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Model;

namespace Gateway.Middleware;

public static class CutoverFreezeExtensions
{
    public static IReverseProxyApplicationBuilder UseCutoverFreeze(this IReverseProxyApplicationBuilder builder)
    {
        builder.Use(next =>
            async context =>
            {
                var clusterId = context.Features.Get<IReverseProxyFeature>()?.Cluster?.Config.ClusterId;
                var options = context
                    .RequestServices.GetRequiredService<IOptionsMonitor<CutoverOptions>>()
                    .CurrentValue;

                if (clusterId is null || !options.FrozenClusters.Contains(clusterId, StringComparer.OrdinalIgnoreCase))
                {
                    await next(context);
                    return;
                }

                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/problem+json";
                context.Response.Headers.RetryAfter = Math.Max(0, options.RetryAfterSeconds)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.Response.Headers["X-Gateway-Cutover"] = "frozen";
                await JsonSerializer.SerializeAsync(
                    context.Response.Body,
                    new
                    {
                        type = "about:blank",
                        title = "Service temporarily unavailable during cutover",
                        status = StatusCodes.Status503ServiceUnavailable,
                        detail = $"The {clusterId} route is temporarily frozen.",
                    },
                    cancellationToken: context.RequestAborted
                );
            }
        );
        return builder;
    }
}
