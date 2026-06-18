using System;
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
            .AddInMemoryClients(Config.Clients)
            .AddAspNetIdentity<User>()
            .AddResourceOwnerValidator<UserValidator>();

        //ref: https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html
        AddSigningCredential(builder, identityServerBuilder, authOptions);

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

    private static void AddSigningCredential(
        WebApplicationBuilder builder,
        IIdentityServerBuilder identityServerBuilder,
        AuthOptions authOptions
    )
    {
        // The developer signing credential generates an ephemeral RSA key (persisted
        // to the gitignored "tempkey.jwk" in the working dir). It is only safe for
        // ephemeral local contexts — local development, the automated test suite, and
        // the local docker-compose demo — where the key never leaves the machine.
        // Using it in any shared or deployed environment would expose the private
        // signing key and allow attackers to forge valid JWTs.
        if (
            builder.Environment.IsDevelopment()
            || builder.Environment.IsEnvironment("test")
            || builder.Environment.IsEnvironment("docker")
        )
        {
            identityServerBuilder.AddDeveloperSigningCredential();
            return;
        }

        var certificate = LoadSigningCertificate(authOptions);
        if (certificate is null)
        {
            throw new InvalidOperationException(
                "No token signing certificate is configured. Provide one via "
                    + "AuthOptions:SigningCertificatePath (with AuthOptions:SigningCertificatePassword) "
                    + "or AuthOptions:SigningCertificateBase64. "
                    + "AddDeveloperSigningCredential() must only be used in the Development or test environments."
            );
        }

        identityServerBuilder.AddSigningCredential(certificate);
    }

    private static X509Certificate2 LoadSigningCertificate(AuthOptions authOptions)
    {
        if (!string.IsNullOrWhiteSpace(authOptions.SigningCertificateBase64))
        {
            var rawData = Convert.FromBase64String(authOptions.SigningCertificateBase64);
            return X509CertificateLoader.LoadPkcs12(rawData, authOptions.SigningCertificatePassword);
        }

        if (!string.IsNullOrWhiteSpace(authOptions.SigningCertificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                authOptions.SigningCertificatePath,
                authOptions.SigningCertificatePassword
            );
        }

        return null;
    }
}
