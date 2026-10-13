using BuildingBlocks.EFCore;
using BuildingBlocks.Mapster;
using BuildingBlocks.Web;
using Duende.IdentityServer.Hosting;
using FluentValidation;
using Identity.Data;
using Identity.Data.Seed;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;

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

        // Authentication/authorization already run earlier in the pipeline (Program.cs). The
        // default UseIdentityServer() adds a second UseAuthentication() that re-runs the default
        // (Identity cookie) scheme and overwrites HttpContext.User AFTER the ApiScope policy has
        // authenticated the bearer token, so handlers would see the cookie's identity instead of
        // the JWT's. Skip that duplicate middleware.
        app.UseIdentityServer(new IdentityServerMiddlewareOptions { AuthenticationMiddleware = _ => { } });
        app.UseMigration<IdentityContext>();

        return app;
    }
}