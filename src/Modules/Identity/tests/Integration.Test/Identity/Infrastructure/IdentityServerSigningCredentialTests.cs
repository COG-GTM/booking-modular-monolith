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
    private const string CompromisedModulus =
        "yaxbLH9EGCPdA0amyVrTqeSnK3_8riIYV_7z9j0SNT1DbUyjKnAvKm2XG-fyDeHc1OyGyprAhbcN8kdBPWgQRFHHnfnvL1S46qnsCy66VrV9lhntHj66wO7fl9niy3AAAhQgq8iXtc__6N3yScR5w82eNHS6eEesQDYMy8vj_5EDN-XDZDNGobANDwD5J-B9pJ84gf-pLftb4xZOSn1-lDSSRObQGn0RIaV0cfv3cyV9S_X26CNr_QjNcHtXPilgTBeMwam3sj932bZwG9nvtf_hmEI9HHUE6-PbiQHpF7-vxUPh2Sv5VzqSrDfJdP6nUvNP_2JUUGk4CebSBJy-pQ";

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
    public async Task production_with_p256_ec_signing_key_without_algorithm_uses_es256()
    {
        var root = CreateContentRoot();
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(ecdsa));
        jwk.Kid = "ec-test-kid";
        jwk.Alg = null;
        var settings = CreateSettings();
        settings["AuthOptions:SigningKey"] = JsonSerializer.Serialize(jwk);

        try
        {
            await using var app = CreateApplication("Production", root, settings);

            var credentialStore = Assert.Single(app.Services.GetServices<ISigningCredentialStore>());
            var credential = await credentialStore.GetSigningCredentialsAsync();

            Assert.Equal(SecurityAlgorithms.EcdsaSha256, credential.Algorithm);
            var signatureProvider = CryptoProviderFactory.Default.CreateForSigning(
                credential.Key,
                credential.Algorithm
            );
            Assert.NotNull(signatureProvider);
            CryptoProviderFactory.Default.ReleaseSignatureProvider(signatureProvider);
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

    [Theory]
    [InlineData("P-384", SecurityAlgorithms.EcdsaSha384)]
    [InlineData("P-521", SecurityAlgorithms.EcdsaSha512)]
    public async Task production_with_ec_signing_key_without_algorithm_uses_curve_algorithm(
        string curve,
        string expectedAlgorithm
    )
    {
        var root = CreateContentRoot();
        using var ecdsa = ECDsa.Create(curve == "P-384" ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP521);
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(ecdsa));
        jwk.Alg = null;
        var settings = CreateSettings();
        settings["AuthOptions:SigningKey"] = JsonSerializer.Serialize(jwk);

        try
        {
            await using var app = CreateApplication("Production", root, settings);
            var credentialStore = Assert.Single(app.Services.GetServices<ISigningCredentialStore>());
            var credential = await credentialStore.GetSigningCredentialsAsync();

            Assert.Equal(expectedAlgorithm, credential.Algorithm);
            var signatureProvider = CryptoProviderFactory.Default.CreateForSigning(
                credential.Key,
                credential.Algorithm
            );
            Assert.NotNull(signatureProvider);
            CryptoProviderFactory.Default.ReleaseSignatureProvider(signatureProvider);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task production_with_explicit_signing_algorithm_honors_configured_algorithm()
    {
        var root = CreateContentRoot();
        var jwk = new JsonWebKey(CreateJwk()) { Alg = SecurityAlgorithms.RsaSha512 };
        var settings = CreateSettings();
        settings["AuthOptions:SigningKey"] = JsonSerializer.Serialize(jwk);

        try
        {
            await using var app = CreateApplication("Production", root, settings);
            var credentialStore = Assert.Single(app.Services.GetServices<ISigningCredentialStore>());
            var credential = await credentialStore.GetSigningCredentialsAsync();

            Assert.Equal(SecurityAlgorithms.RsaSha512, credential.Algorithm);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void signing_algorithm_with_unsupported_key_type_throws_invalid_operation_exception()
    {
        var jwk = new JsonWebKey { Kty = "oct", K = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)) };
        var getSigningAlgorithm = typeof(IdentityServerExtensions).GetMethod(
            "GetSigningAlgorithm",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        );
        Assert.NotNull(getSigningAlgorithm);

        var exception = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => getSigningAlgorithm.Invoke(null, new object[] { jwk })
        );

        var innerException = Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("supported key type and curve", innerException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void production_with_unsupported_ec_curve_throws_invalid_operation_exception()
    {
        var root = CreateContentRoot();
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwk = JsonWebKeyConverter.ConvertFromECDsaSecurityKey(new ECDsaSecurityKey(ecdsa));
        jwk.Crv = "unsupported-curve";
        jwk.Alg = null;
        var settings = CreateSettings();
        settings["AuthOptions:SigningKey"] = JsonSerializer.Serialize(jwk);

        try
        {
            var builder = CreateBuilder("Production", root, settings);

            var exception = Assert.Throws<InvalidOperationException>(() => builder.AddCustomIdentityServer());

            Assert.Contains("supported key type and curve", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task production_with_compromised_public_only_jwk_path_throws_compromised_key_exception()
    {
        var root = CreateContentRoot();
        var settings = CreateSettings();
        var keyPath = Path.Combine(root, "signing-key.jwk");
        settings["AuthOptions:SigningKeyPath"] = keyPath;

        try
        {
            await File.WriteAllTextAsync(keyPath, CreateCompromisedPublicJwk());
            var builder = CreateBuilder("Production", root, settings);

            var exception = Assert.Throws<InvalidOperationException>(() => builder.AddCustomIdentityServer());

            Assert.Contains("previously committed, compromised key", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("private key material", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public void production_with_compromised_public_only_jwk_throws_compromised_key_exception()
    {
        var root = CreateContentRoot();
        var settings = CreateSettings();
        settings["AuthOptions:SigningKey"] = CreateCompromisedPublicJwk();

        try
        {
            var builder = CreateBuilder("Production", root, settings);

            var exception = Assert.Throws<InvalidOperationException>(() => builder.AddCustomIdentityServer());

            Assert.Contains("previously committed, compromised key", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("private key material", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task compromised_developer_key_file_is_rejected()
    {
        var root = CreateContentRoot();
        var path = Path.Combine(root, "tempkey.jwk");
        await File.WriteAllTextAsync(path, CreateCompromisedPublicJwk());

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => IdentityServerExtensions.EnsureDeveloperSigningKeyIsNotCompromised(path)
            );

            Assert.Contains("Delete this file", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task fresh_developer_key_file_is_accepted()
    {
        var root = CreateContentRoot();
        var path = Path.Combine(root, "tempkey.jwk");
        await File.WriteAllTextAsync(path, CreateJwk());

        try
        {
            IdentityServerExtensions.EnsureDeveloperSigningKeyIsNotCompromised(path);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void missing_developer_key_file_is_accepted()
    {
        var root = CreateContentRoot();
        var path = Path.Combine(root, "missing.jwk");

        try
        {
            IdentityServerExtensions.EnsureDeveloperSigningKeyIsNotCompromised(path);
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

    private static string CreateCompromisedPublicJwk()
    {
        return JsonSerializer.Serialize(
            new Dictionary<string, string>
            {
                ["kty"] = "RSA",
                ["kid"] = "5FDEBAED6A154EEFD68A1B54BE25EDC1",
                ["n"] = CompromisedModulus,
                ["e"] = "AQAB",
            }
        );
    }
}
