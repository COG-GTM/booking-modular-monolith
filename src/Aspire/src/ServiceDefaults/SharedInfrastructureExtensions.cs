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

public static class SharedInfrastructureExtensions
{
    public static WebApplicationBuilder AddSharedInfrastructure(
        this WebApplicationBuilder builder,
        TransportType transportType = TransportType.InMemory
    )
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

        builder.Services.AddCustomMassTransit(
            builder.Environment,
            transportType,
            AppDomain.CurrentDomain.GetAssemblies()
        );

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

        var services = builder.Services;
        builder.Services.AddScoped<IEventMapper>(sp =>
        {
            var mapperTypes = services
                .Where(descriptor =>
                    descriptor.ServiceType != typeof(IEventMapper)
                    && typeof(IEventMapper).IsAssignableFrom(descriptor.ServiceType)
                )
                .Select(descriptor => descriptor.ServiceType)
                .Distinct()
                .ToList();

            var mappers = mapperTypes.Select(mapperType => (IEventMapper)sp.GetRequiredService(mapperType)).ToArray();

            return new CompositeEventMapper(mappers);
        });

        return builder;
    }

    public static WebApplication UserSharedInfrastructure(this WebApplication app)
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
