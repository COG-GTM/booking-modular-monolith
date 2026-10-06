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
            .AddInMemoryClients(Config.Clients)
            .AddAspNetIdentity<User>()
            .AddResourceOwnerValidator<UserValidator>();

        //ref: https://docs.duendesoftware.com/identityserver/fundamentals/key-management/
        if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("test"))
        {
            // Generates a local, git-ignored key (tempkey.jwk). It must never be committed or used outside local development.
            identityServerBuilder.AddDeveloperSigningCredential();
        }
        else
        {
            identityServerBuilder.AddSigningCredential(LoadSigningCertificate(authOptions));
        }

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

    private static X509Certificate2 LoadSigningCertificate(AuthOptions authOptions)
    {
        if (string.IsNullOrWhiteSpace(authOptions.SigningCertificatePath))
        {
            throw new InvalidOperationException(
                $"{nameof(AuthOptions)}:{nameof(AuthOptions.SigningCertificatePath)} must be configured outside the Development environment. "
                    + "IdentityServer refuses to start with the developer signing credential."
            );
        }

        if (!File.Exists(authOptions.SigningCertificatePath))
        {
            throw new InvalidOperationException(
                $"IdentityServer signing certificate was not found at '{authOptions.SigningCertificatePath}'."
            );
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            authOptions.SigningCertificatePath,
            authOptions.SigningCertificatePassword
        );

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException("IdentityServer signing certificate must contain a private key.");
        }

        return certificate;
    }
}
