using System.Text.Json;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Configurations;
using Identity.Extensions.Infrastructure;
using Identity.Host.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Identity.Host.Test;

public sealed class IdentitySigningKeyTests : IDisposable
{
    private const string DockerEnvironment = "docker";
    private const string DefaultSigningKeyFile = "tempkey.jwk";

    private readonly string _keysDirectory = Path.Combine(
        Path.GetTempPath(),
        "identity-signing-keys-" + Guid.NewGuid().ToString("N")
    );

    public IdentitySigningKeyTests()
    {
        Directory.CreateDirectory(_keysDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_keysDirectory))
        {
            Directory.Delete(_keysDirectory, recursive: true);
        }
    }

    private static WebApplicationBuilder CreateBuilder(string environment, string? signingKeyPath)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = environment,
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = typeof(Program).Assembly.GetName().Name,
            }
        );
        builder.Configuration.AddInMemoryCollection(
            [
                new("ObservabilityOptions:UseOTLPExporter", "false"),
                new("ObservabilityOptions:UseAspireOTLPExporter", "false"),
                new("ObservabilityOptions:UsePrometheusExporter", "false"),
                new("AuthOptions:SigningKeyPath", signingKeyPath),
            ]
        );

        return builder;
    }

    private static async Task<string> ResolveSigningKeyIdAsync(string environment, string? signingKeyPath)
    {
        var builder = CreateBuilder(environment, signingKeyPath);
        builder.AddIdentityHostInfrastructure();
        builder.AddIdentityModules();

        await using var provider = builder.Services.BuildServiceProvider();
        var credentials = await provider.GetRequiredService<ISigningCredentialStore>().GetSigningCredentialsAsync();

        return credentials.Kid;
    }

    private static string ReadKeyId(string jwkPath)
    {
        using var jwk = JsonDocument.Parse(File.ReadAllText(jwkPath));

        return jwk.RootElement.GetProperty("kid").GetString()!;
    }

    [Theory]
    [InlineData("Production")]
    [InlineData(DockerEnvironment)]
    public void signing_key_path_is_unset_in_host_appsettings(string environment)
    {
        var builder = CreateBuilder(environment, signingKeyPath: null);
        builder.AddIdentityHostInfrastructure();
        builder.AddIdentityModules();

        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<AuthOptions>>().Value.SigningKeyPath.Should().BeNull();
    }

    [Fact]
    public async Task configured_signing_key_path_creates_the_developer_key_at_that_path()
    {
        var signingKeyPath = Path.Combine(_keysDirectory, DefaultSigningKeyFile);

        var keyId = await ResolveSigningKeyIdAsync(DockerEnvironment, signingKeyPath);

        File.Exists(signingKeyPath).Should().BeTrue();
        keyId.Should().Be(ReadKeyId(signingKeyPath));
    }

    [Fact]
    public async Task configured_signing_key_is_reused_when_the_host_is_recreated()
    {
        var signingKeyPath = Path.Combine(_keysDirectory, DefaultSigningKeyFile);

        var firstKeyId = await ResolveSigningKeyIdAsync(DockerEnvironment, signingKeyPath);
        var keyFileAfterFirstStart = await File.ReadAllTextAsync(signingKeyPath);
        var secondKeyId = await ResolveSigningKeyIdAsync(DockerEnvironment, signingKeyPath);

        secondKeyId.Should().Be(firstKeyId);
        (await File.ReadAllTextAsync(signingKeyPath)).Should().Be(keyFileAfterFirstStart);
    }

    [Fact]
    public async Task existing_key_file_at_the_configured_path_is_loaded_instead_of_generating_a_new_one()
    {
        var originalPath = Path.Combine(_keysDirectory, "original.jwk");
        var sharedPath = Path.Combine(_keysDirectory, DefaultSigningKeyFile);
        var originalKeyId = await ResolveSigningKeyIdAsync(Environments.Production, originalPath);
        File.Copy(originalPath, sharedPath);

        var keyId = await ResolveSigningKeyIdAsync(DockerEnvironment, sharedPath);

        keyId.Should().Be(originalKeyId);
    }

    [Fact]
    public async Task different_signing_key_paths_produce_independent_keys()
    {
        var firstKeyId = await ResolveSigningKeyIdAsync(DockerEnvironment, Path.Combine(_keysDirectory, "a.jwk"));
        var secondKeyId = await ResolveSigningKeyIdAsync(DockerEnvironment, Path.Combine(_keysDirectory, "b.jwk"));

        secondKeyId.Should().NotBe(firstKeyId);
    }

    [Fact]
    public async Task signing_key_defaults_to_tempkey_jwk_in_the_working_directory_when_unset()
    {
        var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), DefaultSigningKeyFile);

        var keyId = await ResolveSigningKeyIdAsync(Environments.Production, signingKeyPath: null);

        File.Exists(defaultPath).Should().BeTrue();
        keyId.Should().Be(ReadKeyId(defaultPath));
        Directory.EnumerateFiles(_keysDirectory).Should().BeEmpty();
    }
}
