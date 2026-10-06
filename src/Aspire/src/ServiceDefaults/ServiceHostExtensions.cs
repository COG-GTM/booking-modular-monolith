using System.Reflection;
using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Cross-cutting infrastructure shared by every host (the modular monolith and the per-module services):
/// service defaults (health, OTEL, service discovery), JWT bearer auth, outbox processor, OpenAPI,
/// API versioning, MassTransit over RabbitMQ, gRPC and problem details.
/// </summary>
public static class ServiceHostExtensions
{
    public static WebApplicationBuilder AddServiceHostInfrastructure(
        this WebApplicationBuilder builder,
        params Assembly[] messagingAssemblies)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();
        builder.AddPersistMessageProcessor();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventDispatcher, EventDispatcher>();

        builder.Services.AddCustomMassTransit(builder.Environment, messagingAssemblies);

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });

        builder.Services.AddEasyCaching(options =>
        {
            options.UseInMemory(builder.Configuration, "mem");
        });
        builder.Services.AddProblemDetails();

        return builder;
    }

    /// <summary>
    /// Registers the module's <see cref="IEventMapper"/> as the host-wide mapper. Hosts that compose several
    /// modules should register a <see cref="CompositeEventMapper"/> instead.
    /// </summary>
    public static IServiceCollection AddModuleEventMapper<TEventMapper>(this IServiceCollection services)
        where TEventMapper : class, IEventMapper
    {
        services.AddScoped<IEventMapper>(sp => sp.GetRequiredService<TEventMapper>());

        return services;
    }

    public static WebApplication UseServiceHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseServiceDefaults();

        app.UseCustomProblemDetails();

        app.UseCorrelationId();

        app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
