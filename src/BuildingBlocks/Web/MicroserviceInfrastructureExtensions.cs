using BuildingBlocks.Core;
using BuildingBlocks.Jwt;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.Web;

public static class MicroserviceInfrastructureExtensions
{
    /// <summary>
    /// Registers common infrastructure services shared by all microservices.
    /// Each service should additionally call AddServiceDefaults(), AddGrpc(), and
    /// its own module registration (e.g. AddFlightModules()) in Program.cs.
    /// </summary>
    public static WebApplicationBuilder AddMicroserviceInfrastructure(this WebApplicationBuilder builder)
    {
        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventDispatcher, EventDispatcher>();

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddEasyCaching(options =>
        {
            options.UseInMemory(builder.Configuration, "mem");
        });

        builder.Services.AddProblemDetails();

        return builder;
    }

    public static WebApplication UseMicroserviceInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

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
