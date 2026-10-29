using System.Threading.RateLimiting;
using BuildingBlocks.Jwt;
using Gateway.Middleware;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy;

namespace Gateway.Extensions;

public static class GatewayExtensions
{
    public const string ReverseProxySection = "ReverseProxy";

    public static WebApplicationBuilder AddGateway(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        var cutoverConfigFile = builder.Configuration["Cutover:ConfigFile"];
        if (!string.IsNullOrWhiteSpace(cutoverConfigFile))
            builder.Configuration.AddJsonFile(cutoverConfigFile, optional: true, reloadOnChange: true);

        builder.Services.AddJwt();

        // Bound lazily (IOptions) so CORS/rate-limit settings come from the final configuration, env vars included.
        builder.Services.Configure<GatewayCorsOptions>(builder.Configuration.GetSection("Cors"));
        builder.Services.Configure<GatewayRateLimitOptions>(builder.Configuration.GetSection("RateLimiting"));
        builder.Services.Configure<CutoverOptions>(builder.Configuration.GetSection("Cutover"));

        builder.Services.AddCors();
        builder
            .Services.AddOptions<CorsOptions>()
            .Configure<IOptions<GatewayCorsOptions>>(
                (cors, gatewayCors) =>
                    cors.AddPolicy(
                        GatewayCorsOptions.PolicyName,
                        policy =>
                            policy
                                .WithOrigins(gatewayCors.Value.AllowedOrigins)
                                .AllowAnyHeader()
                                .AllowAnyMethod()
                                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)
                    )
            );

        builder.Services.AddGatewayRateLimiting();

        builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection(ReverseProxySection));

        return builder;
    }

    public static WebApplication UseGateway(this WebApplication app)
    {
        app.UseServiceDefaults();

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<GatewayRequestLoggingMiddleware>();

        app.UseRouting();
        // Applied to every request so preflights are answered here, before authentication.
        app.UseCors(GatewayCorsOptions.PolicyName);
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        app.MapReverseProxy(proxyPipeline =>
        {
            proxyPipeline.UseCutoverFreeze();
            proxyPipeline.UseSessionAffinity();
            proxyPipeline.UseLoadBalancing();
            proxyPipeline.UsePassiveHealthChecks();
        });

        return app;
    }

    private static IServiceCollection AddGatewayRateLimiting(this IServiceCollection services)
    {
        return services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    );
                }

                return ValueTask.CompletedTask;
            };

            limiter.AddPolicy(
                GatewayRateLimitOptions.PolicyName,
                httpContext =>
                {
                    var options = httpContext
                        .RequestServices.GetRequiredService<IOptions<GatewayRateLimitOptions>>()
                        .Value;

                    if (!options.Enabled)
                        return RateLimitPartition.GetNoLimiter("disabled");

                    // Partition per authenticated subject, falling back to the client IP.
                    var partitionKey =
                        httpContext.User.FindFirst("sub")?.Value
                        ?? httpContext.Connection.RemoteIpAddress?.ToString()
                        ?? "anonymous";

                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = options.PermitLimit,
                            Window = TimeSpan.FromSeconds(options.WindowSeconds),
                            QueueLimit = options.QueueLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        }
                    );
                }
            );
        });
    }
}
