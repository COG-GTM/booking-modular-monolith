using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Grpc;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Flight;
using Microsoft.AspNetCore.Mvc;

namespace Flight.Host.Extensions;

public static class FlightServiceInfrastructureExtensions
{
    public static WebApplicationBuilder AddFlightServiceInfrastructure(this WebApplicationBuilder builder)
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

        builder.Services.AddCustomMassTransit(
            builder.Environment,
            TransportType.RabbitMq,
            typeof(FlightRoot).Assembly
        );

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });
        builder.Services.AddGrpcHealthService();

        builder.Services.AddEasyCaching(options =>
        {
            options.UseInMemory(builder.Configuration, "mem");
        });
        builder.Services.AddProblemDetails();

        builder.Services.AddScoped<FlightEventMapper>();
        builder.Services.AddScoped<IEventMapper>(sp => sp.GetRequiredService<FlightEventMapper>());

        return builder;
    }

    public static WebApplication UseFlightServiceInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseServiceDefaults();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();

        app.MapGet("/", response => response.Response.WriteAsync(appOptions.Name));
        app.MapGrpcHealthService();

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
