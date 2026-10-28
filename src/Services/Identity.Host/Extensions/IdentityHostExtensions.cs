using System.Text.Json;
using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using HealthChecks.NpgSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Identity.Host.Extensions;

public static class IdentityHostExtensions
{
    public const string ServiceName = "identity-service";
    public const string ModuleName = "Identity";
    private const string ReadyTag = "ready";

    public static WebApplicationBuilder AddIdentityHostInfrastructure(this WebApplicationBuilder builder)
    {
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
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventHeadersProvider, HttpEventHeadersProvider>();
        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddCustomMassTransit(
            builder.Environment,
            builder.Configuration,
            ServiceName,
            typeof(IdentityRoot).Assembly
        );
        builder.Services.AddEventMapper<IdentityEventMapper>();

        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();

        builder
            .Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"])
            .Add(
                new HealthCheckRegistration(
                    "postgres-identity",
                    sp => new NpgSqlHealthCheck(
                        new NpgSqlHealthCheckOptions(
                            sp.GetRequiredService<IConfiguration>().ResolvePostgresConnectionString(ModuleName)
                        )
                    ),
                    HealthStatus.Unhealthy,
                    [ReadyTag]
                )
            )
            .Add(
                new HealthCheckRegistration(
                    "postgres-identity-persist-message",
                    sp => new NpgSqlHealthCheck(
                        new NpgSqlHealthCheckOptions(
                            sp.GetRequiredService<IConfiguration>().ResolvePersistMessageConnectionString(ModuleName)
                        )
                    ),
                    HealthStatus.Unhealthy,
                    [ReadyTag]
                )
            );

        return builder;
    }

    public static WebApplication UseIdentityHostInfrastructure(this WebApplication app)
    {
        app.UseCustomObservability();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();

        app.MapHealthChecks(
                "/health/live",
                new HealthCheckOptions { Predicate = r => r.Tags.Contains("live"), ResponseWriter = WriteHealthAsync }
            )
            .AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { ResponseWriter = WriteHealthAsync })
            .AllowAnonymous();

        app.MapGet("/", () => ServiceName).AllowAnonymous();

        if (app.Environment.IsDevelopment())
            app.UseAspnetOpenApi();

        return app;
    }

    private static Task WriteHealthAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(
                new
                {
                    status = report.Status.ToString(),
                    checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString()),
                }
            )
        );
    }
}
