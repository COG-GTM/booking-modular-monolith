using System.Security.Cryptography;
using System.Text.Json;
using Duende.IdentityServer.Configuration;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Data;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

public class IdentityServerSigningConfigurationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"identity-configuration-tests-{Guid.NewGuid():N}");

    public IdentityServerSigningConfigurationTests()
    {
        Directory.CreateDirectory(root);
    }

    [Theory]
    [InlineData("test", true)]
    [InlineData("Staging", false)]
    [InlineData("docker", false)]
    public async Task missing_key_selects_credentials_for_environment(string environment, bool developerCredential)
    {
        var developerKeyPath = Path.Combine(Directory.GetCurrentDirectory(), "tempkey.jwk");
        var developerKeyExisted = File.Exists(developerKeyPath);
        var developerKeyLastWriteTimeUtc = developerKeyExisted ? File.GetLastWriteTimeUtc(developerKeyPath) : default;

        await using var app = CreateBuilder(environment).AddCustomIdentityServer().Build();

        app.Services.GetServices<ISigningCredentialStore>().Should().HaveCount(developerCredential ? 1 : 0);
        if (!developerCredential)
        {
            app.Services.GetRequiredService<IOptions<IdentityServerOptions>>()
                .Value.KeyManagement.Enabled.Should()
                .BeTrue();
            File.Exists(developerKeyPath).Should().Be(developerKeyExisted);
            if (developerKeyExisted)
            {
                File.GetLastWriteTimeUtc(developerKeyPath).Should().Be(developerKeyLastWriteTimeUtc);
            }
        }
    }

    [Theory]
    [InlineData("Development", null)]
    [InlineData("test", "RS512")]
    [InlineData("Production", null)]
    public async Task inline_key_overrides_missing_key_path_and_environment(string environment, string? algorithm)
    {
        using var rsa = RSA.Create(2048);
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa));
        key.Kid = "configured-key";
        key.Alg = algorithm;
        var builder = CreateBuilder(environment, JsonSerializer.Serialize(key), Path.Combine(root, "missing.jwk"));
        await using var app = builder.AddCustomIdentityServer().Build();

        var store = app.Services.GetServices<ISigningCredentialStore>().Should().ContainSingle().Subject;
        var credential = await store.GetSigningCredentialsAsync();
        credential.Key.KeyId.Should().Be("configured-key");
        credential.Algorithm.Should().Be(algorithm ?? SecurityAlgorithms.RsaSha256);
        app.Services.GetRequiredService<IOptions<IdentityServerOptions>>()
            .Value.KeyManagement.Enabled.Should()
            .BeFalse();
    }

    [Fact]
    public async Task whitespace_inline_key_falls_back_to_key_path()
    {
        using var rsa = RSA.Create(2048);
        var key = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa));
        key.Kid = "file-key";
        var path = Path.Combine(root, "configured.jwk");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(key));
        await using var app = CreateBuilder("Production", "  ", path).AddCustomIdentityServer().Build();

        var store = app.Services.GetServices<ISigningCredentialStore>().Should().ContainSingle().Subject;
        (await store.GetSigningCredentialsAsync()).Key.KeyId.Should().Be("file-key");
    }

    [Fact]
    public async Task whitespace_key_settings_use_automatic_key_management()
    {
        await using var app = CreateBuilder("Production", "  ", "  ").AddCustomIdentityServer().Build();

        app.Services.GetServices<ISigningCredentialStore>().Should().BeEmpty();
        app.Services.GetRequiredService<IOptions<IdentityServerOptions>>()
            .Value.KeyManagement.Enabled.Should()
            .BeTrue();
    }

    [Fact]
    public void missing_configured_key_file_throws_instead_of_using_developer_credential()
    {
        var builder = CreateBuilder("Development", path: Path.Combine(root, "missing.jwk"));

        Action configure = () => builder.AddCustomIdentityServer();

        configure.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void malformed_inline_key_throws_instead_of_using_developer_credential()
    {
        const string malformedKey = "not-json";
        var builder = CreateBuilder("Development", malformedKey);

        Action configure = () => builder.AddCustomIdentityServer();

        var exception = Assert.Throws<InvalidOperationException>(configure);

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(malformedKey, exception.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        Directory.Delete(root, true);
    }

    private WebApplicationBuilder CreateBuilder(string environment, string? key = null, string? path = null)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = environment, ContentRootPath = root }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AuthOptions:IssuerUri"] = "https://identity.test",
                ["AuthOptions:SigningKey"] = key,
                ["AuthOptions:SigningKeyPath"] = path,
            }
        );
        builder.Services.AddDbContext<IdentityContext>();
        return builder;
    }
}
