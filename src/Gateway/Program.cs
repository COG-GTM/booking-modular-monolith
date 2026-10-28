using System.Threading.RateLimiting;
using BuildingBlocks.Jwt;
using BuildingBlocks.OpenTelemetryCollector;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.AddCustomObservability();
builder.Services.AddJwt();
builder.Services.Configure<JwtBearerOptions>(
    JwtBearerDefaults.AuthenticationScheme,
    options =>
    {
        var metadata = builder.Configuration["Jwt:MetadataAddress"];
        if (!string.IsNullOrWhiteSpace(metadata))
            options.MetadataAddress = metadata;
    }
);
builder.Services.AddAuthorization(options =>
    options.AddPolicy("authenticated", policy => policy.RequireAuthenticatedUser())
);
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (origins.Length > 0)
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    })
);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "ingress",
        context =>
            RateLimitPartition.GetFixedWindowLimiter(
                context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = builder.Configuration.GetValue("RateLimiting:PermitLimit", 100),
                    Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimiting:WindowSeconds", 60)),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }
            )
    );
});
builder.Services.AddHealthChecks();
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();
app.Use(
    async (context, next) =>
    {
        var correlation = context.Request.Headers["X-Correlation-ID"].ToString();
        if (string.IsNullOrWhiteSpace(correlation) || correlation.Length > 128)
            correlation = context.TraceIdentifier;
        context.Request.Headers["X-Correlation-ID"] = correlation;
        context.Response.Headers["X-Correlation-ID"] = correlation;
        var started = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await next(context);
        }
        finally
        {
            app.Logger.LogInformation(
                "{Method} {Path} returned {StatusCode} in {ElapsedMs} ms; correlation {CorrelationId}",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                started.ElapsedMilliseconds,
                correlation
            );
        }
    }
);
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapReverseProxy().RequireRateLimiting("ingress");
app.Run();

public partial class Program { }
