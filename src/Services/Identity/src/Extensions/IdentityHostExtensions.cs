using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using IdentityEventMapper = global::Identity.IdentityEventMapper;

namespace Identity.Host.Extensions;

public static class IdentityHostExtensions
{
    public static WebApplicationBuilder AddIdentityHostInfrastructure(this WebApplicationBuilder builder)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventHeadersProvider, HttpContextEventHeadersProvider>();
        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            assembly: [typeof(global::Identity.IdentityRoot).Assembly]
        );
        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();
        builder.Services.AddScoped<IdentityEventMapper>();
        builder.Services.AddScoped<IEventMapper>(sp => sp.GetRequiredService<IdentityEventMapper>());

        return builder;
    }

    public static WebApplication UseIdentityHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseServiceDefaults();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();
        app.MapGet("/", () => appOptions.Name);

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
