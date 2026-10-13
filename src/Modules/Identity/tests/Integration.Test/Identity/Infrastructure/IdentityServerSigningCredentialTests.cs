using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

// The token signing credential is chosen by AddCustomIdentityServer from AuthOptions:SigningCertificate
// and the host environment. No database or container is needed to verify that wiring, so these tests
// build the host directly instead of going through the Testcontainers-backed fixture.
public class IdentityServerSigningCredentialTests : IDisposable
{
    private const string Password = "p12-password";

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "identity-signing-credential-tests",
        Guid.NewGuid().ToString("N")
    );

    public IdentityServerSigningCredentialTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private ServiceProvider BuildIdentityServer(
        string environmentName,
        string certificatePath = null,
        string certificatePassword = null
    )
    {
        var contentRoot = Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environmentName, ContentRootPath = contentRoot }
        );
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string>
            {
                { "AuthOptions:IssuerUri", "http://localhost:5000" },
                { "AuthOptions:SigningCertificate:Path", certificatePath },
                { "AuthOptions:SigningCertificate:Password", certificatePassword },
            }
        );

        builder.AddCustomIdentityServer();

        return builder.Services.BuildServiceProvider();
    }

    private string WriteSelfSignedPkcs12(string fileName, string password = Password)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=booking-modular-monolith-signing",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1)
        );

        var path = Path.Combine(_directory, fileName);
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pkcs12, password));
        return path;
    }

    private static X509Certificate2 CertificateOf(string path, string password = Password) =>
        X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);

    private static async Task<SigningCredentials> SigningCredentialsOf(ServiceProvider provider) =>
        await provider.GetRequiredService<ISigningCredentialStore>().GetSigningCredentialsAsync();

    private static bool HasAnyFileUnder(string contentRoot) =>
        Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories).Any();

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("test")]
    [InlineData("docker")]
    public async Task a_configured_pkcs12_certificate_is_used_as_the_signing_credential_in_every_environment(
        string environmentName
    )
    {
        var pfx = WriteSelfSignedPkcs12("signing.pfx");
        using var expected = CertificateOf(pfx);
        await using var provider = BuildIdentityServer(environmentName, pfx, Password);

        var credentials = await SigningCredentialsOf(provider);

        credentials.Algorithm.Should().Be(SecurityAlgorithms.RsaSha256);
        credentials
            .Key.Should()
            .BeOfType<X509SecurityKey>()
            .Which.Certificate.Thumbprint.Should()
            .Be(expected.Thumbprint, "tokens must be signed with the configured certificate");
    }

    [Fact]
    public async Task the_configured_certificate_is_also_published_as_a_validation_key()
    {
        var pfx = WriteSelfSignedPkcs12("signing.pfx");
        using var expected = CertificateOf(pfx);
        await using var provider = BuildIdentityServer("Production", pfx, Password);

        var validationKeys = await provider.GetRequiredService<IValidationKeysStore>().GetValidationKeysAsync();

        validationKeys
            .Should()
            .ContainSingle(k =>
                k.Key is X509SecurityKey && ((X509SecurityKey)k.Key).Certificate.Thumbprint == expected.Thumbprint
            )
            .Which.SigningAlgorithm.Should()
            .Be(SecurityAlgorithms.RsaSha256);
    }

    [Fact]
    public async Task hosts_configured_with_the_same_certificate_sign_with_the_same_key()
    {
        var pfx = WriteSelfSignedPkcs12("shared.pfx");
        await using var monolith = BuildIdentityServer("Production", pfx, Password);
        await using var identityService = BuildIdentityServer("Production", pfx, Password);

        var monolithKid = (await SigningCredentialsOf(monolith)).Key.KeyId;
        var serviceKid = (await SigningCredentialsOf(identityService)).Key.KeyId;

        monolithKid.Should().NotBeNullOrWhiteSpace();
        serviceKid.Should().Be(monolithKid);
    }

    [Fact]
    public void a_wrong_certificate_password_fails_at_startup()
    {
        var pfx = WriteSelfSignedPkcs12("signing.pfx");

        var act = () => BuildIdentityServer("Production", pfx, "not-the-password");

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void a_missing_certificate_file_fails_at_startup()
    {
        var missing = Path.Combine(_directory, "does-not-exist.pfx");

        var act = () => BuildIdentityServer("Production", missing, Password);

        act.Should().Throw<Exception>().Which.Should().Match(e => e is IOException || e is CryptographicException);
    }

    [Fact]
    public void a_certificate_without_a_private_key_is_rejected()
    {
        var pfx = WriteSelfSignedPkcs12("with-key.pfx");
        using var withKey = CertificateOf(pfx);
        using var publicOnly = X509CertificateLoader.LoadCertificate(withKey.Export(X509ContentType.Cert));
        publicOnly.HasPrivateKey.Should().BeFalse();
        var publicOnlyPfx = Path.Combine(_directory, "public-only.pfx");
        File.WriteAllBytes(publicOnlyPfx, publicOnly.Export(X509ContentType.Pkcs12, Password));

        var act = () => BuildIdentityServer("Production", publicOnlyPfx, Password);

        act.Should().Throw<InvalidOperationException>("a certificate without its private key cannot sign tokens");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("test")]
    public async Task development_and_test_fall_back_to_an_in_memory_developer_key_that_is_never_written_to_disk(
        string environmentName
    )
    {
        await using var provider = BuildIdentityServer(environmentName);

        var credentials = await SigningCredentialsOf(provider);

        credentials.Key.KeyId.Should().NotBeNullOrWhiteSpace();
        credentials.Key.Should().NotBeOfType<X509SecurityKey>();
        HasAnyFileUnder(_directory)
            .Should()
            .BeFalse("the developer key must stay in memory so it can never be committed again");
    }

    [Fact]
    public async Task each_development_host_generates_its_own_developer_key()
    {
        await using var first = BuildIdentityServer("Development");
        await using var second = BuildIdentityServer("Development");

        var firstKid = (await SigningCredentialsOf(first)).Key.KeyId;
        var secondKid = (await SigningCredentialsOf(second)).Key.KeyId;

        secondKid.Should().NotBe(firstKid, "nothing is persisted, so every start gets a fresh key");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("docker")]
    public void other_environments_refuse_to_start_without_a_configured_certificate(string environmentName)
    {
        var act = () => BuildIdentityServer(environmentName);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*environment '{environmentName}'*")
            .WithMessage(
                $"*{nameof(AuthOptions)}:{nameof(AuthOptions.SigningCertificate)}:{nameof(SigningCertificateOptions.Path)}*"
            );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void a_blank_certificate_path_counts_as_not_configured(string certificatePath)
    {
        var act = () => BuildIdentityServer("Production", certificatePath, Password);

        act.Should().Throw<InvalidOperationException>().WithMessage("*No token signing credential is configured*");
    }

    [Fact]
    public void the_environment_check_is_case_insensitive()
    {
        var act = () => BuildIdentityServer("development");

        act.Should().NotThrow("IHostEnvironment.IsDevelopment() compares environment names case-insensitively");
    }
}
