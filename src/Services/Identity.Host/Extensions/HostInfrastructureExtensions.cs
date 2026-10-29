using BuildingBlocks.Caching;
using BuildingBlocks.Core;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Identity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Host.Extensions;

public static class HostInfrastructureExtensions
{
    public static WebApplicationBuilder AddIdentityHostInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddEventDispatcher();
        builder.Services.AddHttpContextEventHeaders();

        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            configureBus: null,
            typeof(IdentityRoot).Assembly
        );

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();

        return builder;
    }

    public static WebApplication UseIdentityHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseServiceDefaults();

        if (!app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks(
                "/alive",
                new HealthCheckOptions { Predicate = registration => registration.Tags.Contains("live") }
            );
        }

        app.UseCustomProblemDetails();
        app.UseCorrelationId();
        app.MapGet("/", context => context.Response.WriteAsync(appOptions.Name));

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
