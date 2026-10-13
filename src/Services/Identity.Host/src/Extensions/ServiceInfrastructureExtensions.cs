using BuildingBlocks.Core;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Identity.Host.Extensions;

// Standalone equivalent of the monolith's shared infrastructure, scoped to the Identity module only.
public static class ServiceInfrastructureExtensions
{
    public static WebApplicationBuilder AddIdentityServiceInfrastructure(this WebApplicationBuilder builder)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.Services.AddCustomHealthCheck();
        builder.AddCustomObservability();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddEventDispatcher();
        builder.Services.AddHttpContextEventHeaders();

        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            typeof(IdentityRoot).Assembly
        );

        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();

        return builder;
    }

    public static WebApplication UseIdentityServiceInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseCustomHealthCheck();
        app.MapProbeHealthChecks();
        app.UseCustomObservability();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();

        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }

    // UseCustomHealthCheck only maps /health and /alive in Development; docker/k8s probe every environment.
    private static WebApplication MapProbeHealthChecks(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
            return app;

        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });

        return app;
    }
}
