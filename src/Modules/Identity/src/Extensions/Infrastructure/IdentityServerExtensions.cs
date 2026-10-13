using BuildingBlocks.Web;
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
                // One shared key file means one key: automatic key management would otherwise create a
                // per-host key next to it and the hosts' JWKS would differ.
                if (!string.IsNullOrWhiteSpace(authOptions.SigningKeyPath))
                    options.KeyManagement.Enabled = false;
            })
            .AddInMemoryIdentityResources(Config.IdentityResources)
            .AddInMemoryApiResources(Config.ApiResources)
            .AddInMemoryApiScopes(Config.ApiScopes)
            .AddInMemoryClients(Config.Clients)
            .AddAspNetIdentity<User>()
            .AddResourceOwnerValidator<UserValidator>();

        var defaultKeyFile = Path.Combine(builder.Environment.ContentRootPath, "tempkey.jwk");
        var keyFile = string.IsNullOrWhiteSpace(authOptions.SigningKeyPath)
            ? defaultKeyFile
            : Path.GetFullPath(authOptions.SigningKeyPath);
        if (keyFile != defaultKeyFile)
            SeedSharedSigningKey(defaultKeyFile, keyFile);

        //ref: https://documentation.openiddict.com/configuration/encryption-and-signing-credentials.html
        identityServerBuilder.AddDeveloperSigningCredential(persistKey: true, filename: keyFile);

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

    // First start with a shared key path: carry over the key this host was already signing with (its
    // content-root tempkey.jwk) so tokens issued before the switch keep validating.
    private static void SeedSharedSigningKey(string currentKeyFile, string sharedKeyFile)
    {
        if (File.Exists(currentKeyFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(sharedKeyFile)!);
            try
            {
                // CreateNew is atomic (O_EXCL): exactly one racing host wins and seeds the shared file.
                using var shared = new FileStream(sharedKeyFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var own = File.OpenRead(currentKeyFile);
                own.CopyTo(shared);
                return;
            }
            catch (IOException) when (File.Exists(sharedKeyFile))
            {
                // Another host seeded the shared file first; use that one.
            }
        }

        if (File.Exists(sharedKeyFile))
            WaitUntilFullyWritten(sharedKeyFile);
    }

    // The winner still holds the shared file exclusively while copying; a loser that reads it right away
    // either fails with "being used by another process" or sees a truncated key, so wait for the winner.
    private static void WaitUntilFullyWritten(string file)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length > 0)
                    return;
            }
            catch (IOException)
            {
                // Still being written; retry below.
            }

            if (DateTime.UtcNow > deadline)
                return;

            Thread.Sleep(25);
        }
    }
}
