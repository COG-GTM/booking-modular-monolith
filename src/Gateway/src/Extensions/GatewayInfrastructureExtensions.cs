using System.Threading.RateLimiting;
using BuildingBlocks.Jwt;
using BuildingBlocks.Web;
using Gateway.Configurations;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Yarp.ReverseProxy.Transforms;

namespace Gateway.Extensions;

public static class GatewayInfrastructureExtensions
{
    public const string RateLimiterPolicyName = "ingress";
    private const string CorrelationIdHeader = "correlationId";

    public static WebApplicationBuilder AddGatewayInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddCustomForwardedHeaders();
        builder.Services.AddCustomCors();
        builder.Services.AddCustomRateLimiter();
        builder.Services.AddCustomRequestLogging();

        builder
            .Services.AddReverseProxy()
            .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
            .AddTransforms(context => context.AddRequestTransform(PropagateCorrelationId));

        return builder;
    }

    public static WebApplication UseGatewayInfrastructure(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseServiceDefaults();
        app.UseCorrelationId();
        app.UseHttpLogging();
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGatewayHealthChecks();
        app.MapReverseProxy();

        return app;
    }

    // ServiceDefaults only exposes /health and /alive in Development; the gateway is probed by
    // docker/Aspire in every environment and must answer itself instead of proxying the probe.
    private static WebApplication MapGatewayHealthChecks(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            return app;

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });

        return app;
    }

    // Behind a load balancer the peer address is the balancer's; only proxies listed here are
    // trusted to supply X-Forwarded-For, which the rate limiter then partitions on.
    private static IServiceCollection AddCustomForwardedHeaders(this IServiceCollection services)
    {
        var trustedProxies = services.GetOptions<TrustedProxyOptions>(nameof(TrustedProxyOptions));

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            foreach (var proxy in trustedProxies.KnownProxies)
                options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));

            foreach (var network in trustedProxies.KnownNetworks)
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });

        return services;
    }

    private static IServiceCollection AddCustomCors(this IServiceCollection services)
    {
        var corsOptions = services.GetOptions<CorsOptions>(nameof(CorsOptions));

        services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyHeader().AllowAnyMethod();

                if (corsOptions.AllowedOrigins.Length == 0)
                    policy.AllowAnyOrigin();
                else
                    policy.WithOrigins(corsOptions.AllowedOrigins).AllowCredentials();
            })
        );

        return services;
    }

    private static IServiceCollection AddCustomRateLimiter(this IServiceCollection services)
    {
        var rateLimitOptions = services.GetOptions<RateLimitOptions>(nameof(RateLimitOptions));

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(
                RateLimiterPolicyName,
                httpContext =>
                    RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        factory: _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = rateLimitOptions.PermitLimit,
                            Window = TimeSpan.FromSeconds(rateLimitOptions.WindowSeconds),
                            QueueLimit = rateLimitOptions.QueueLimit,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        }
                    )
            );
        });

        return services;
    }

    private static IServiceCollection AddCustomRequestLogging(this IServiceCollection services)
    {
        services.AddHttpLogging(options =>
        {
            options.LoggingFields =
                HttpLoggingFields.RequestMethod
                | HttpLoggingFields.RequestPath
                | HttpLoggingFields.RequestQuery
                | HttpLoggingFields.RequestProtocol
                | HttpLoggingFields.ResponseStatusCode
                | HttpLoggingFields.Duration;
            options.CombineLogs = true;
        });

        return services;
    }

    private static ValueTask PropagateCorrelationId(RequestTransformContext context)
    {
        if (
            context.HttpContext.Items.TryGetValue(CorrelationIdHeader, out var correlationId)
            && correlationId is string value
        )
        {
            context.ProxyRequest.Headers.Remove(CorrelationIdHeader);
            context.ProxyRequest.Headers.TryAddWithoutValidation(CorrelationIdHeader, value);
        }

        return ValueTask.CompletedTask;
    }
}
