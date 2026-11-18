using System.Security.Cryptography;
using BuildingBlocks.Web;
using Identity.Data;
using Identity.Identity.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

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
                options.KeyManagement.Enabled = false;
            })
            .AddInMemoryIdentityResources(Config.IdentityResources)
            .AddInMemoryApiResources(Config.ApiResources)
            .AddInMemoryApiScopes(Config.ApiScopes)
            .AddInMemoryClients(Config.Clients)
            .AddAspNetIdentity<User>()
            .AddResourceOwnerValidator<UserValidator>();

        identityServerBuilder.AddTokenSigningCredential(builder.Environment, authOptions);

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

    private static void AddTokenSigningCredential(
        this IIdentityServerBuilder identityServerBuilder,
        IHostEnvironment environment,
        AuthOptions authOptions)
    {
        if (!string.IsNullOrWhiteSpace(authOptions.SigningKeyPath))
        {
            identityServerBuilder.AddSigningCredential(
                LoadRsaSigningKey(authOptions.SigningKeyPath),
                SecurityAlgorithms.RsaSha256);

            return;
        }

        if (environment.IsDevelopment())
        {
            // Never named tempkey.jwk so a leaked copy of the old committed key is not picked up.
            identityServerBuilder.AddDeveloperSigningCredential(
                persistKey: true,
                filename: Path.Combine(environment.ContentRootPath, DevelopmentSigningKeyFileName));

            return;
        }

        if (environment.IsEnvironment("test") || environment.IsEnvironment("docker"))
        {
            identityServerBuilder.AddDeveloperSigningCredential(persistKey: false);

            return;
        }

        throw new InvalidOperationException(
            $"{nameof(AuthOptions)}:{nameof(AuthOptions.SigningKeyPath)} must point to a PEM-encoded RSA private key " +
            $"in the '{environment.EnvironmentName}' environment.");
    }

    private static RsaSecurityKey LoadRsaSigningKey(string path)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(path));

        var key = new RsaSecurityKey(rsa);
        key.KeyId = Base64UrlEncoder.Encode(key.ComputeJwkThumbprint());

        return key;
    }

    private const string DevelopmentSigningKeyFileName = "identityserver-dev-signing-key.jwk";
}