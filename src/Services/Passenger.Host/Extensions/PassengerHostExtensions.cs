using System.Reflection;
using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Passenger;
using Passenger.Host.Health;

namespace Passenger.Host.Extensions;

public static class PassengerHostExtensions
{
    public static WebApplicationBuilder AddPassengerHostInfrastructure(this WebApplicationBuilder builder)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
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
            PassengerConsumerAssemblies(builder.Configuration)
        );
        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });
        builder.Services.AddGrpcHealthChecks(options =>
        {
            options.Services.Clear();
            options.Services.Map(string.Empty, _ => true);
            options.Services.Map(PassengerGrpcService.Descriptor.FullName, _ => true);
        });
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();
        builder.Services.AddPassengerHealthChecks(builder.Configuration);

        return builder;
    }

    internal static Assembly[] PassengerConsumerAssemblies(IConfiguration configuration) =>
        ModuleBackgroundProcessing.ConsumerAssemblies(
            configuration,
            ("Passenger", typeof(global::Passenger.PassengerRoot).Assembly)
        );

    public static WebApplication UsePassengerHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseServiceDefaults();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();
        app.MapGrpcHealthChecksService();
        app.MapGet("/", () => Results.Text(appOptions.Name));

        if (app.Environment.IsDevelopment())
            app.UseAspnetOpenApi();

        return app;
    }
}
