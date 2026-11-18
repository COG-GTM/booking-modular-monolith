using System.IO;
using System.Text.Json;
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
    private const string CompromisedSigningKeyThumbprint = "oPrXK0pKCMY3OTrfOWp7cEewMLlHKSygX1jmGBdycxQ";

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
            jwk = ParseJsonWebKey(
                signingKeyJson,
                "The configured signing JWK could not be parsed. Check AuthOptions:SigningKey or AuthOptions:SigningKeyPath."
            );
            if (IsPreviouslyCommittedCompromisedKey(jwk))
            {
                throw new InvalidOperationException(
                    "The configured signing key is the previously committed, compromised key; generate a new one."
                );
            }

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
            var algorithm = GetSigningAlgorithm(jwk);
            jwk.Alg = algorithm;
            identityServerBuilder.AddSigningCredential(jwk, algorithm);
        }
        else if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("test"))
        {
            EnsureDeveloperSigningKeyIsNotCompromised(Path.Combine(Directory.GetCurrentDirectory(), "tempkey.jwk"));
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

    private static JsonWebKey ParseJsonWebKey(string json, string errorMessage)
    {
        try
        {
            return new JsonWebKey(json);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(errorMessage);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(errorMessage);
        }
        catch (SecurityTokenException)
        {
            throw new InvalidOperationException(errorMessage);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(errorMessage);
        }
    }

    internal static void EnsureDeveloperSigningKeyIsNotCompromised(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var jwk = ParseJsonWebKey(
            File.ReadAllText(path),
            "The developer signing key file could not be parsed. Delete it so a new key is generated."
        );
        if (IsPreviouslyCommittedCompromisedKey(jwk))
        {
            throw new InvalidOperationException(
                "The developer signing key file is the previously committed, compromised key. Delete this file so a new key is generated."
            );
        }
    }

    private static bool IsPreviouslyCommittedCompromisedKey(JsonWebKey jwk)
    {
        return Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint()) == CompromisedSigningKeyThumbprint;
    }

    private static string GetSigningAlgorithm(JsonWebKey jwk)
    {
        var algorithm = jwk.Alg;
        if (string.IsNullOrWhiteSpace(algorithm))
        {
            algorithm = jwk.Kty switch
            {
                "RSA" => SecurityAlgorithms.RsaSha256,
                "EC" => jwk.Crv switch
                {
                    "P-256" => SecurityAlgorithms.EcdsaSha256,
                    "P-384" => SecurityAlgorithms.EcdsaSha384,
                    "P-521" => SecurityAlgorithms.EcdsaSha512,
                    _ => throw UnsupportedSigningAlgorithm(),
                },
                _ => throw UnsupportedSigningAlgorithm(),
            };
        }

        var isCompatible = algorithm switch
        {
            SecurityAlgorithms.RsaSha256
            or SecurityAlgorithms.RsaSha384
            or SecurityAlgorithms.RsaSha512
            or SecurityAlgorithms.RsaSsaPssSha256
            or SecurityAlgorithms.RsaSsaPssSha384
            or SecurityAlgorithms.RsaSsaPssSha512 => jwk.Kty == "RSA",
            SecurityAlgorithms.EcdsaSha256 => jwk.Kty == "EC" && jwk.Crv == "P-256",
            SecurityAlgorithms.EcdsaSha384 => jwk.Kty == "EC" && jwk.Crv == "P-384",
            SecurityAlgorithms.EcdsaSha512 => jwk.Kty == "EC" && jwk.Crv == "P-521",
            _ => false,
        };

        return isCompatible ? algorithm : throw UnsupportedSigningAlgorithm();
    }

    private static InvalidOperationException UnsupportedSigningAlgorithm()
    {
        return new InvalidOperationException(
            "The configured signing JWK must specify an algorithm compatible with its key type and curve. Set AuthOptions:SigningKey or AuthOptions:SigningKeyPath."
        );
    }
}
