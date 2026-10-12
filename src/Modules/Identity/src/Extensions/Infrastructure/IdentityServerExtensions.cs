using System.Security.Cryptography.X509Certificates;
using BuildingBlocks.Web;
using Identity.Data;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Identity.Extensions.Infrastructure;

using Configurations;

public static class IdentityServerExtensions
{
    public static WebApplicationBuilder AddCustomIdentityServer(this WebApplicationBuilder builder)
    {
        builder.Services.AddValidateOptions<AuthOptions>();
        var authOptions = builder.Services.GetOptions<AuthOptions>(nameof(AuthOptions));

        builder
            .Services.AddIdentity<User, Role>(config =>
            {
                config.Password.RequiredLength = 6;
                config.Password.RequireDigit = false;
                config.Password.RequireNonAlphanumeric = false;
                config.Password.RequireUppercase = false;
            })
            .AddEntityFrameworkStores<IdentityContext>()
            .AddDefaultTokenProviders();

        var identityServerBuilder = builder
            .Services.AddIdentityServer(options =>
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
            .AddInMemoryClients(Config.GetClients(authOptions))
            .AddAspNetIdentity<User>()
            .AddResourceOwnerValidator<UserValidator>();

        identityServerBuilder.AddCustomSigningCredential(builder.Environment, authOptions);

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

    private static IIdentityServerBuilder AddCustomSigningCredential(
        this IIdentityServerBuilder identityServerBuilder,
        IHostEnvironment environment,
        AuthOptions authOptions
    )
    {
        if (!string.IsNullOrWhiteSpace(authOptions.SigningCertificatePath))
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                authOptions.SigningCertificatePath,
                authOptions.SigningCertificatePassword
            );

            return identityServerBuilder.AddSigningCredential(certificate);
        }

        // The developer signing credential is a temp key written to disk (tempkey.jwk).
        // It is only acceptable for local development and automated tests.
        if (environment.IsDevelopment() || environment.IsEnvironment("test"))
        {
            return identityServerBuilder.AddDeveloperSigningCredential();
        }

        throw new InvalidOperationException(
            $"{nameof(AuthOptions)}:{nameof(AuthOptions.SigningCertificatePath)} must be configured in the '{environment.EnvironmentName}' environment. "
                + "The developer signing credential is only allowed in Development."
        );
    }
}
