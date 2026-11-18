using System.Security.Cryptography;
using System.Text.Json;
using Duende.IdentityServer.Configuration;
using Duende.IdentityServer.Stores;
using Identity.Data;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityServerSigningCredentialTests
{
    [Fact]
    public async Task production_with_inline_signing_key_uses_configured_credential()
    {
        var root = CreateContentRoot();
        var settings = CreateSettings();
        settings["AuthOptions:SigningKey"] = CreateJwk();

        try
        {
            await using var app = CreateApplication("Production", root, settings);

            await AssertConfiguredCredential(app);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task production_with_signing_key_path_uses_configured_credential()
    {
        var root = CreateContentRoot();
        var keyPath = Path.Combine(root, "signing-key.jwk");
        await File.WriteAllTextAsync(keyPath, CreateJwk());
        var settings = CreateSettings();
        settings["AuthOptions:SigningKeyPath"] = keyPath;

        try
        {
            await using var app = CreateApplication("Production", root, settings);

            await AssertConfiguredCredential(app);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task production_without_signing_key_uses_automatic_key_management()
    {
        var root = CreateContentRoot();

        try
        {
            await using var app = CreateApplication("Production", root, CreateSettings());

            Assert.Empty(app.Services.GetServices<ISigningCredentialStore>());
            Assert.True(app.Services.GetRequiredService<IOptions<IdentityServerOptions>>().Value.KeyManagement.Enabled);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task development_without_signing_key_uses_developer_credential()
    {
        var root = CreateContentRoot();

        try
        {
            await using var app = CreateApplication("Development", root, CreateSettings());

            Assert.NotEmpty(app.Services.GetServices<ISigningCredentialStore>());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void production_with_public_only_jwk_throws_invalid_operation_exception()
    {
        var root = CreateContentRoot();
        var settings = CreateSettings();
        settings["AuthOptions:SigningKey"] = CreateJwk(includePrivateKey: false);

        try
        {
            var builder = CreateBuilder("Production", root, settings);

            var exception = Assert.Throws<InvalidOperationException>(() => builder.AddCustomIdentityServer());

            Assert.Contains("AuthOptions:SigningKey", exception.Message, StringComparison.Ordinal);
            Assert.Contains("AuthOptions:SigningKeyPath", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static async Task AssertConfiguredCredential(WebApplication app)
    {
        var credentialStore = Assert.Single(app.Services.GetServices<ISigningCredentialStore>());
        var credentials = await credentialStore.GetSigningCredentialsAsync();

        Assert.Equal("test-kid", credentials.Key.KeyId);
        Assert.False(app.Services.GetRequiredService<IOptions<IdentityServerOptions>>().Value.KeyManagement.Enabled);
    }

    private static WebApplication CreateApplication(
        string environmentName,
        string contentRoot,
        Dictionary<string, string?> settings
    )
    {
        var builder = CreateBuilder(environmentName, contentRoot, settings);
        builder.AddCustomIdentityServer();
        return builder.Build();
    }

    private static WebApplicationBuilder CreateBuilder(
        string environmentName,
        string contentRoot,
        Dictionary<string, string?> settings
    )
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environmentName, ContentRootPath = contentRoot }
        );
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddDbContext<IdentityContext>();
        return builder;
    }

    private static Dictionary<string, string?> CreateSettings()
    {
        return new Dictionary<string, string?> { ["AuthOptions:IssuerUri"] = "https://identity.test" };
    }

    private static string CreateContentRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"identity-signing-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateJwk(bool includePrivateKey = true)
    {
        using var rsa = RSA.Create(2048);
        var parameters = rsa.ExportParameters(true);
        var values = new Dictionary<string, string>
        {
            ["kty"] = "RSA",
            ["kid"] = "test-kid",
            ["alg"] = "RS256",
            ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
            ["e"] = Base64UrlEncoder.Encode(parameters.Exponent),
        };

        if (includePrivateKey)
        {
            values["d"] = Base64UrlEncoder.Encode(parameters.D);
            values["p"] = Base64UrlEncoder.Encode(parameters.P);
            values["q"] = Base64UrlEncoder.Encode(parameters.Q);
            values["dp"] = Base64UrlEncoder.Encode(parameters.DP);
            values["dq"] = Base64UrlEncoder.Encode(parameters.DQ);
            values["qi"] = Base64UrlEncoder.Encode(parameters.InverseQ);
        }

        return JsonSerializer.Serialize(values);
    }
}
