using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.HealthCheck;

// Check names and statuses only; failure descriptions can leak dependency details, so they are Development-only.
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var includeDescriptions =
            context.RequestServices.GetService<IHostEnvironment>()?.IsDevelopment() ?? false;

        return context.Response.WriteAsJsonAsync(
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new
                    {
                        status = entry.Value.Status.ToString(),
                        description = includeDescriptions ? entry.Value.Description : null,
                    }
                ),
            }
        );
    }
}
