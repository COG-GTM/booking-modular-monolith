using System.Reflection;
using Booking;
using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Flight;
using Identity;
using Microsoft.AspNetCore.Mvc;
using Passenger;

namespace Api.Extensions;

public static class SharedInfrastructureExtensions
{
    public static WebApplicationBuilder AddSharedInfrastructure(this WebApplicationBuilder builder)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

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
            TransportType.InMemory,
            GetMessagingAssemblies(builder.Configuration)
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

        builder.Services.AddScoped<IEventMapper>(sp =>
        {
            var mappers = new IEventMapper[]
            {
                sp.GetRequiredService<FlightEventMapper>(),
                sp.GetRequiredService<IdentityEventMapper>(),
                sp.GetRequiredService<PassengerEventMapper>(),
                sp.GetRequiredService<BookingEventMapper>(),
            };

            return new CompositeEventMapper(mappers);
        });

        return builder;
    }

    // Consumers of a module whose background processing is switched off (its service host owns them)
    // are not registered in the monolith.
    private static Assembly[] GetMessagingAssemblies(IConfiguration configuration)
    {
        var moduleAssemblies = new Dictionary<string, Assembly>
        {
            [nameof(Flight)] = typeof(FlightRoot).Assembly,
            [nameof(Identity)] = typeof(IdentityRoot).Assembly,
            [nameof(Passenger)] = typeof(PassengerRoot).Assembly,
            [nameof(Booking)] = typeof(BookingRoot).Assembly,
        };

        var disabledAssemblies = moduleAssemblies
            .Where(module => !configuration.IsModuleBackgroundProcessingEnabled(module.Key))
            .Select(module => module.Value)
            .ToHashSet();

        return AppDomain.CurrentDomain.GetAssemblies().Where(a => !disabledAssemblies.Contains(a)).ToArray();
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