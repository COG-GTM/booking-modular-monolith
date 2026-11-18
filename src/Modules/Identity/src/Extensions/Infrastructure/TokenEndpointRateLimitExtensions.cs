using System;
using System.Threading.RateLimiting;
using BuildingBlocks.Web;
using Identity.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Extensions.Infrastructure;

public static class TokenEndpointRateLimitExtensions
{
    public static readonly PathString TokenEndpointPath = new("/connect/token");

    // Duende IdentityServer serves /connect/token from middleware rather than a routed endpoint,
    // so the limit is applied as a global limiter scoped to that path.
    public static WebApplicationBuilder AddTokenEndpointRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidateOptions<TokenEndpointRateLimitOptions>();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                if (!context.Request.Path.StartsWithSegments(TokenEndpointPath, StringComparison.OrdinalIgnoreCase))
                {
                    return RateLimitPartition.GetNoLimiter(string.Empty);
                }

                var settings = context.RequestServices.GetRequiredService<TokenEndpointRateLimitOptions>();
                var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    clientIp,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
        });

        return builder;
    }
}
