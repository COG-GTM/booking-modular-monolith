using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Duende.IdentityServer.Configuration;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityServerSigningKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private ServiceProvider BuildIdentityServer(string environment, string signingKeyPath = null)
    {
        Directory.CreateDirectory(_directory);
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = _directory }
        );
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string>
            {
                ["AuthOptions:IssuerUri"] = "http://localhost:5000",
                ["AuthOptions:SigningKeyPath"] = signingKeyPath,
            }
        );
        builder.AddCustomIdentityServer();
        return builder.Services.BuildServiceProvider();
    }

    private static Task<SigningCredentials> Credentials(ServiceProvider provider) =>
        provider.GetRequiredService<ISigningCredentialStore>().GetSigningCredentialsAsync();

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Staging", "")]
    [InlineData("Production", "   ")]
    public void non_development_environment_requires_a_signing_key(string environment, string path)
    {
        Action configure = () => BuildIdentityServer(environment, path);

        configure
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*AuthOptions:SigningKeyPath*'{environment}'*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("test")]
    [InlineData("docker")]
    [InlineData("Production")]
    public async Task configured_pem_takes_precedence_and_uses_rs256_with_a_thumbprint_id(string environment)
    {
        Directory.CreateDirectory(_directory);
        using var rsa = RSA.Create(2048);
        var path = Path.Combine(_directory, "signing.pem");
        File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
        await using var provider = BuildIdentityServer(environment, path);

        var credentials = await Credentials(provider);

        credentials.Algorithm.Should().Be(SecurityAlgorithms.RsaSha256);
        credentials.Key.KeyId.Should().Be(Base64UrlEncoder.Encode(new RsaSecurityKey(rsa).ComputeJwkThumbprint()));
        using var publicKey = RSA.Create();
        publicKey.ImportParameters(rsa.ExportParameters(false));
        var data = new byte[] { 1, 2, 3 };
        var factory = credentials.Key.CryptoProviderFactory;
        var signature = factory.CreateForSigning(credentials.Key, credentials.Algorithm);
        try
        {
            publicKey
                .VerifyData(data, signature.Sign(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                .Should()
                .BeTrue();
        }
        finally
        {
            factory.ReleaseSignatureProvider(signature);
        }
        provider.GetRequiredService<IdentityServerOptions>().KeyManagement.Enabled.Should().BeFalse();
        Directory.GetFiles(_directory).Should().ContainSingle().Which.Should().Be(path);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("docker")]
    public async Task ephemeral_environments_ignore_legacy_keys_and_never_persist_keys(string environment)
    {
        Directory.CreateDirectory(_directory);
        var legacyPath = Path.Combine(_directory, "tempkey.jwk");
        File.WriteAllText(legacyPath, "not a valid legacy key");
        await using var first = BuildIdentityServer(environment);
        await using var second = BuildIdentityServer(environment);

        var firstCredentials = await Credentials(first);
        var secondCredentials = await Credentials(second);

        firstCredentials.Key.KeyId.Should().NotBe(secondCredentials.Key.KeyId);
        Directory.GetFiles(_directory).Should().ContainSingle().Which.Should().Be(legacyPath);
        File.ReadAllText(legacyPath).Should().Be("not a valid legacy key");
        first.GetRequiredService<IdentityServerOptions>().KeyManagement.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task development_persists_a_separate_key_and_reuses_it_on_restart()
    {
        Directory.CreateDirectory(_directory);
        var legacyPath = Path.Combine(_directory, "tempkey.jwk");
        File.WriteAllText(legacyPath, "not a valid legacy key");
        await using var first = BuildIdentityServer("Development");
        var firstCredentials = await Credentials(first);
        await using var second = BuildIdentityServer("Development");

        (await Credentials(second)).Key.KeyId.Should().Be(firstCredentials.Key.KeyId);
        File.Exists(Path.Combine(_directory, "identityserver-dev-signing-key.jwk")).Should().BeTrue();
        File.ReadAllText(legacyPath).Should().Be("not a valid legacy key");
        first.GetRequiredService<IdentityServerOptions>().KeyManagement.Enabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void missing_configured_file_fails_instead_of_falling_back(string environment)
    {
        Action configure = () => BuildIdentityServer(environment, Path.Combine(_directory, "missing.pem"));

        configure.Should().Throw<FileNotFoundException>();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void malformed_configured_pem_fails_instead_of_falling_back(string environment)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "invalid.pem");
        File.WriteAllText(path, "not a PEM key");
        Action configure = () => BuildIdentityServer(environment, path);

        configure.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
