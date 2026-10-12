using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Unit.Test.Extensions;

public class IdentityServerExtensionsTests : IDisposable
{
    private const string ClientSecret = "unit-test-client-secret";

    private readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "identity-unit-test",
        Guid.NewGuid().ToString("N")
    );

    public IdentityServerExtensionsTests()
    {
        Directory.CreateDirectory(_workingDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private WebApplicationBuilder CreateBuilder(string environmentName, Dictionary<string, string> authOptions)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environmentName, ContentRootPath = _workingDirectory }
        );

        builder.Configuration.AddInMemoryCollection(
            authOptions.ToDictionary(x => $"{nameof(AuthOptions)}:{x.Key}", x => x.Value)
        );

        return builder;
    }

    private string CreateSigningCertificate(string password, out X509Certificate2 certificate)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=unit-test-signing",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));

        var path = Path.Combine(_workingDirectory, "signing.pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        return path;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void should_refuse_to_start_when_client_secret_is_not_configured(string environmentName)
    {
        var builder = CreateBuilder(environmentName, new Dictionary<string, string> { ["ClientId"] = "client" });

        var act = () => builder.AddCustomIdentityServer();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(AuthOptions)}:{nameof(AuthOptions.ClientSecret)}*");
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void should_refuse_to_use_the_developer_signing_credential_outside_development(string environmentName)
    {
        var builder = CreateBuilder(
            environmentName,
            new Dictionary<string, string> { ["ClientId"] = "client", ["ClientSecret"] = ClientSecret }
        );

        var act = () => builder.AddCustomIdentityServer();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{nameof(AuthOptions)}:{nameof(AuthOptions.SigningCertificatePath)}*")
            .WithMessage($"*{environmentName}*");
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("test")]
    public async Task should_use_the_developer_signing_credential_in_development_and_test(string environmentName)
    {
        var builder = CreateBuilder(
            environmentName,
            new Dictionary<string, string> { ["ClientId"] = "client", ["ClientSecret"] = ClientSecret }
        );

        builder.AddCustomIdentityServer();

        await using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var credentials = await scope
            .ServiceProvider.GetRequiredService<ISigningCredentialStore>()
            .GetSigningCredentialsAsync();

        credentials.Should().NotBeNull();
        credentials.Key.Should().NotBeOfType<X509SecurityKey>();
    }

    [Fact]
    public async Task should_use_the_configured_pkcs12_certificate_as_signing_credential()
    {
        const string certificatePassword = "pfx-password";
        var certificatePath = CreateSigningCertificate(certificatePassword, out var certificate);
        var builder = CreateBuilder(
            "Production",
            new Dictionary<string, string>
            {
                ["ClientId"] = "client",
                ["ClientSecret"] = ClientSecret,
                ["SigningCertificatePath"] = certificatePath,
                ["SigningCertificatePassword"] = certificatePassword,
            }
        );

        builder.AddCustomIdentityServer();

        await using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var credentials = await scope
            .ServiceProvider.GetRequiredService<ISigningCredentialStore>()
            .GetSigningCredentialsAsync();

        credentials
            .Key.Should()
            .BeOfType<X509SecurityKey>()
            .Which.Certificate.Thumbprint.Should()
            .Be(certificate.Thumbprint);
    }

    [Fact]
    public void should_fail_when_the_configured_signing_certificate_password_is_wrong()
    {
        var certificatePath = CreateSigningCertificate("correct-password", out _);
        var builder = CreateBuilder(
            "Production",
            new Dictionary<string, string>
            {
                ["ClientId"] = "client",
                ["ClientSecret"] = ClientSecret,
                ["SigningCertificatePath"] = certificatePath,
                ["SigningCertificatePassword"] = "wrong-password",
            }
        );

        var act = () => builder.AddCustomIdentityServer();

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task should_register_the_client_from_configuration()
    {
        var builder = CreateBuilder(
            "Development",
            new Dictionary<string, string> { ["ClientId"] = "configured-client", ["ClientSecret"] = ClientSecret }
        );

        builder.AddCustomIdentityServer();

        await using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var clientStore = scope.ServiceProvider.GetRequiredService<IClientStore>();

        var client = await clientStore.FindClientByIdAsync("configured-client");
        client.Should().NotBeNull();
        client.ClientSecrets.Should().ContainSingle().Which.Value.Should().Be(ClientSecret.Sha256());

        (await clientStore.FindClientByIdAsync("client")).Should().BeNull();
    }

    [Fact]
    public async Task should_bind_auth_options_from_configuration()
    {
        var builder = CreateBuilder(
            "Development",
            new Dictionary<string, string>
            {
                ["ClientId"] = "configured-client",
                ["ClientSecret"] = ClientSecret,
                ["SeedDefaultUsers"] = "true",
                ["SeedAdminPassword"] = "admin-pass",
                ["SeedUserPassword"] = "user-pass",
            }
        );

        builder.AddCustomIdentityServer();

        await using var provider = builder.Services.BuildServiceProvider();
        var authOptions = provider.GetRequiredService<AuthOptions>();

        authOptions.ClientId.Should().Be("configured-client");
        authOptions.ClientSecret.Should().Be(ClientSecret);
        authOptions.SeedDefaultUsers.Should().BeTrue();
        authOptions.SeedAdminPassword.Should().Be("admin-pass");
        authOptions.SeedUserPassword.Should().Be("user-pass");
    }
}
