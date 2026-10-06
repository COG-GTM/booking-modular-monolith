using BuildingBlocks.Web;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Stores;
using Identity.Data;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Extensions.Infrastructure;

using Configurations;

public static class IdentityServerExtensions
{
    public static WebApplicationBuilder AddCustomIdentityServer(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidateOptions<AuthOptions>();
        builder.Services.AddOptions<AuthOptions>().ValidateOnStart();
        var authOptions = builder.Services.GetOptions<AuthOptions>(nameof(AuthOptions));

        builder.Services.AddIdentity<User, Role>(config =>
            {
                config.Password.RequiredLength = 6;
                config.Password.RequireDigit = false;
                config.Password.RequireNonAlphanumeric = false;
                config.Password.RequireUppercase = false;
            })
            .AddEntityFrameworkStores<IdentityContext>()
            .AddDefaultTokenProviders();

        var identityServerBuilder = builder.Services.AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;
                options.IssuerUri = authOptions.IssuerUri;
            })
            .AddInMemoryIdentityResources(Config.IdentityResources)
            .AddInMemoryApiResources(Config.ApiResources)
            .AddInMemoryApiScopes(Config.ApiScopes)
            .AddClientStore<InMemoryClientStore>()
            .AddCorsPolicyService<InMemoryCorsPolicyService>()
            .AddAspNetIdentity<User>()
            .AddResourceOwnerValidator<UserValidator>();

        // Clients are resolved lazily so the client secret comes from the fully built configuration
        // (AuthOptions:ClientSecret); AuthOptions validation fails startup when it is missing.
        builder.Services.AddSingleton<IEnumerable<Client>>(sp => Config.GetClients(sp.GetRequiredService<AuthOptions>()));

        //ref: https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html
        identityServerBuilder.AddDeveloperSigningCredential();

        builder.Services.ConfigureApplicationCookie(options =>
                                                    {
                                                        options.Events.OnRedirectToLogin = context =>
                                                        {
                                                            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                                                            return Task.CompletedTask;
                                                        };

                                                        options.Events.OnRedirectToAccessDenied = context =>
                                                        {
                                                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                                                            return Task.CompletedTask;
                                                        };
                                                    });

        return builder;
    }
}