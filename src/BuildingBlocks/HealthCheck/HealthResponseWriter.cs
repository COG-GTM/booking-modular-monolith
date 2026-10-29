using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.HealthCheck;

public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report) =>
        context.Response.WriteAsJsonAsync(
            new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(
                    entry => entry.Key,
                    entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description }
                ),
            }
        );
}
