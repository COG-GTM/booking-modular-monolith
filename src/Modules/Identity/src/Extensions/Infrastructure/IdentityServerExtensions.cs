using System.IO;
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
        var signingKeyJson =
            !string.IsNullOrWhiteSpace(authOptions.SigningKey) ? authOptions.SigningKey
            : !string.IsNullOrWhiteSpace(authOptions.SigningKeyPath) ? File.ReadAllText(authOptions.SigningKeyPath)
            : null;
        JsonWebKey jwk = null;

        if (signingKeyJson is not null)
        {
            jwk = new JsonWebKey(signingKeyJson);
            if (!jwk.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    "The configured signing JWK must include private key material. Set AuthOptions:SigningKey or AuthOptions:SigningKeyPath to a private JWK."
                );
            }
        }

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
                if (jwk is not null)
                {
                    options.KeyManagement.Enabled = false;
                }
            })
            .AddInMemoryIdentityResources(Config.IdentityResources)
            .AddInMemoryApiResources(Config.ApiResources)
            .AddInMemoryApiScopes(Config.ApiScopes)
            .AddInMemoryClients(Config.Clients)
            .AddAspNetIdentity<User>()
            .AddResourceOwnerValidator<UserValidator>();

        if (jwk is not null)
        {
            identityServerBuilder.AddSigningCredential(
                jwk,
                string.IsNullOrEmpty(jwk.Alg) ? SecurityAlgorithms.RsaSha256 : jwk.Alg
            );
        }
        else if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("test"))
        {
            identityServerBuilder.AddDeveloperSigningCredential();
        }
        else
        {
            // Duende automatic key management handles non-development environments.
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
}
