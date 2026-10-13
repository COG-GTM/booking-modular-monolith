using System.IO;
using System.Threading.Tasks;
using Duende.IdentityServer.Configuration;
using Duende.IdentityServer.Stores;
using FluentAssertions;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

// Cut-over window: the monolith and identity-service must sign tokens with the same key, so
// AuthOptions:SigningKeyPath points both hosts at one shared key file. No database or container
// is needed to verify the wiring, so these tests build the host directly.
public class IdentityServerSigningKeyTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "identity-signing-key-tests",
        Guid.NewGuid().ToString("N")
    );

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    // Like the cut-over compose overlay, the directory holding the key is provided by the host
    // (a shared volume); AddDeveloperSigningCredential only creates the file.
    private static ServiceProvider BuildIdentityServer(string signingKeyPath)
    {
        if (!string.IsNullOrWhiteSpace(signingKeyPath))
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(signingKeyPath))!);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string>
            {
                { "AuthOptions:IssuerUri", "http://localhost:5000" },
                { "AuthOptions:SigningKeyPath", signingKeyPath },
            }
        );

        builder.AddCustomIdentityServer();

        return builder.Services.BuildServiceProvider();
    }

    private static async Task<string> SigningKeyIdOf(ServiceProvider provider)
    {
        var credentials = await provider.GetRequiredService<ISigningCredentialStore>().GetSigningCredentialsAsync();
        return credentials.Key.KeyId;
    }

    [Fact]
    public async Task signing_key_is_persisted_to_the_configured_path()
    {
        var keyFile = Path.Combine(_directory, "keys", "signing.jwk");
        await using var provider = BuildIdentityServer(keyFile);

        var kid = await SigningKeyIdOf(provider);

        kid.Should().NotBeNullOrWhiteSpace();
        File.Exists(keyFile).Should().BeTrue("the developer signing credential must be written to SigningKeyPath");
    }

    [Fact]
    public async Task hosts_sharing_a_signing_key_path_issue_tokens_with_the_same_key()
    {
        var keyFile = Path.Combine(_directory, "shared", "tempkey.jwk");
        await using var monolith = BuildIdentityServer(keyFile);
        await using var identityService = BuildIdentityServer(keyFile);

        var monolithKid = await SigningKeyIdOf(monolith);
        var serviceKid = await SigningKeyIdOf(identityService);

        serviceKid.Should().Be(monolithKid);
    }

    [Fact]
    public async Task hosts_with_different_signing_key_paths_use_different_keys()
    {
        await using var first = BuildIdentityServer(Path.Combine(_directory, "a", "tempkey.jwk"));
        await using var second = BuildIdentityServer(Path.Combine(_directory, "b", "tempkey.jwk"));

        var firstKid = await SigningKeyIdOf(first);
        var secondKid = await SigningKeyIdOf(second);

        secondKid.Should().NotBe(firstKid);
    }

    [Fact]
    public async Task automatic_key_management_keys_are_stored_next_to_the_signing_key_file()
    {
        var keyFile = Path.Combine(_directory, "shared", "tempkey.jwk");
        await using var provider = BuildIdentityServer(keyFile);

        var options = provider.GetRequiredService<IdentityServerOptions>();

        options.KeyManagement.KeyPath.Should().Be(Path.GetDirectoryName(Path.GetFullPath(keyFile)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task without_a_signing_key_path_the_key_management_directory_is_left_at_its_default(
        string signingKeyPath
    )
    {
        await using var provider = BuildIdentityServer(signingKeyPath);

        var options = provider.GetRequiredService<IdentityServerOptions>();

        options.KeyManagement.KeyPath.Should().Be(new KeyManagementOptions().KeyPath);
        options.IssuerUri.Should().Be("http://localhost:5000");
    }
}
