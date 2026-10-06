using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityServerSigningCredentialTests
{
    [Fact]
    public void should_fail_closed_when_no_signing_certificate_is_configured_outside_development()
    {
        var builder = CreateBuilder(Environments.Production, new Dictionary<string, string?>());

        var act = () => builder.AddCustomIdentityServer();

        act.Should().Throw<InvalidOperationException>().WithMessage("*SigningCertificatePath*");
    }

    [Fact]
    public async Task should_sign_with_configured_certificate_outside_development()
    {
        var certificatePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        using var certificate = CreateSelfSignedCertificate();
        await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pkcs12, "test-password"));

        try
        {
            var builder = CreateBuilder(
                Environments.Production,
                new Dictionary<string, string?>
                {
                    ["AuthOptions:SigningCertificatePath"] = certificatePath,
                    ["AuthOptions:SigningCertificatePassword"] = "test-password",
                }
            );

            builder.AddCustomIdentityServer();

            await using var provider = builder.Services.BuildServiceProvider();
            var credential = await provider.GetRequiredService<ISigningCredentialStore>().GetSigningCredentialsAsync();

            credential
                .Key.Should()
                .BeOfType<X509SecurityKey>()
                .Which.Certificate.Thumbprint.Should()
                .Be(certificate.Thumbprint);
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    [Fact]
    public void should_fail_closed_when_configured_signing_certificate_file_does_not_exist()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        var builder = CreateBuilder(
            Environments.Production,
            new Dictionary<string, string?> { ["AuthOptions:SigningCertificatePath"] = missingPath }
        );

        var act = () => builder.AddCustomIdentityServer();

        act.Should().Throw<InvalidOperationException>().WithMessage($"*was not found at '{missingPath}'*");
    }

    [Fact]
    public async Task should_fail_closed_when_signing_certificate_password_is_wrong()
    {
        var certificatePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        using var certificate = CreateSelfSignedCertificate();
        await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pkcs12, "test-password"));

        try
        {
            var builder = CreateBuilder(
                Environments.Production,
                new Dictionary<string, string?>
                {
                    ["AuthOptions:SigningCertificatePath"] = certificatePath,
                    ["AuthOptions:SigningCertificatePassword"] = "wrong-password",
                }
            );

            var act = () => builder.AddCustomIdentityServer();

            act.Should().Throw<CryptographicException>();
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    [Fact]
    public async Task should_fail_closed_when_signing_certificate_has_no_private_key()
    {
        var certificatePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        using var certificate = CreateSelfSignedCertificate();
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
        await File.WriteAllBytesAsync(certificatePath, publicOnly.Export(X509ContentType.Pkcs12, "test-password"));

        try
        {
            var builder = CreateBuilder(
                Environments.Production,
                new Dictionary<string, string?>
                {
                    ["AuthOptions:SigningCertificatePath"] = certificatePath,
                    ["AuthOptions:SigningCertificatePassword"] = "test-password",
                }
            );

            var act = () => builder.AddCustomIdentityServer();

            act.Should().Throw<InvalidOperationException>().WithMessage("*must contain a private key*");
        }
        finally
        {
            File.Delete(certificatePath);
        }
    }

    [Fact]
    public async Task should_not_use_configured_certificate_in_development()
    {
        var builder = CreateBuilder(
            Environments.Development,
            new Dictionary<string, string?>
            {
                ["AuthOptions:SigningCertificatePath"] = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx"),
            }
        );

        builder.AddCustomIdentityServer();

        await using var provider = builder.Services.BuildServiceProvider();
        var credential = await provider.GetRequiredService<ISigningCredentialStore>().GetSigningCredentialsAsync();

        credential.Key.Should().NotBeOfType<X509SecurityKey>();
    }

    [Fact]
    public void should_allow_developer_signing_credential_in_test_environment()
    {
        var builder = CreateBuilder("test", new Dictionary<string, string?>());

        var act = () => builder.AddCustomIdentityServer();

        act.Should().NotThrow();
    }

    [Fact]
    public void should_allow_developer_signing_credential_in_development()
    {
        var builder = CreateBuilder(Environments.Development, new Dictionary<string, string?>());

        var act = () => builder.AddCustomIdentityServer();

        act.Should().NotThrow();
    }

    private static WebApplicationBuilder CreateBuilder(string environment, Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=identityserver-signing-test",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }
}
