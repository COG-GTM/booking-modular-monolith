using BuildingBlocks.EFCore;
using BuildingBlocks.Mapster;
using BuildingBlocks.Web;
using FluentValidation;
using Identity.Configurations;
using Identity.Data;
using Identity.Data.Seed;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Identity.Extensions.Infrastructure;


public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddIdentityModules(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IdentityEventMapper>();
        builder.AddMinimalEndpoints(assemblies: typeof(IdentityRoot).Assembly);
        builder.Services.AddValidatorsFromAssembly(typeof(IdentityRoot).Assembly);
        builder.Services.AddCustomMapster(typeof(IdentityRoot).Assembly);
        builder.AddCustomDbContext<IdentityContext>(nameof(Identity));
        builder.Services.AddValidateOptions<IdentitySeedOptions>();
        builder.Services.AddScoped<IDataSeeder, IdentityDataSeeder>();
        builder.AddCustomIdentityServer();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });

        builder.Services.AddCustomMediatR();

        return builder;
    }


    public static WebApplication UseIdentityModules(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseIdentityServer();

        var authOptions = app.GetOptions<AuthOptions>(nameof(AuthOptions));
        if (!authOptions.HasClientCredentials)
        {
            app.Logger.LogWarning(
                "AuthOptions:ClientSecret is not configured. No IdentityServer client is registered and /connect/token will reject all requests.");
        }

        app.UseMigration<IdentityContext>();

        return app;
    }
}