using System.IO;
using System.Linq;
using System.Threading;
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
    // Each host gets its own content root (where the default tempkey.jwk lives) unless the test shares one.
    private ServiceProvider BuildIdentityServer(string signingKeyPath, string contentRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(signingKeyPath))
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(signingKeyPath))!);
        contentRoot ??= Path.Combine(_directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = contentRoot }
        );
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
    public async Task automatic_key_management_is_turned_off_when_a_signing_key_path_is_set()
    {
        var keyFile = Path.Combine(_directory, "shared", "tempkey.jwk");
        await using var provider = BuildIdentityServer(keyFile);

        var options = provider.GetRequiredService<IdentityServerOptions>();

        options.KeyManagement.Enabled.Should().BeFalse("a per-host generated key would make the hosts' JWKS differ");
    }

    [Fact]
    public async Task first_start_with_a_signing_key_path_carries_over_the_host_s_existing_key()
    {
        var contentRoot = Path.Combine(_directory, "host");
        var existing = Path.Combine(contentRoot, "tempkey.jwk");
        await using (var before = BuildIdentityServer(null, contentRoot))
            (await SigningKeyIdOf(before)).Should().NotBeNullOrEmpty();
        File.Exists(existing).Should().BeTrue();
        var previousKid = await SigningKeyIdOf(BuildIdentityServer(null, contentRoot));

        var shared = Path.Combine(_directory, "shared", "tempkey.jwk");
        await using var after = BuildIdentityServer(shared, contentRoot);

        (await SigningKeyIdOf(after))
            .Should()
            .Be(previousKid, "the shared key file is seeded from the host's current key");
        File.ReadAllText(shared).Should().Be(File.ReadAllText(existing));
    }

    [Fact]
    public async Task an_existing_shared_key_is_not_overwritten_by_a_host_s_own_key()
    {
        var shared = Path.Combine(_directory, "shared", "tempkey.jwk");
        var sharedKid = await SigningKeyIdOf(BuildIdentityServer(shared));
        var contentRoot = Path.Combine(_directory, "host2");
        await using (var own = BuildIdentityServer(null, contentRoot))
            (await SigningKeyIdOf(own)).Should().NotBe(sharedKid);

        await using var joined = BuildIdentityServer(shared, contentRoot);

        (await SigningKeyIdOf(joined)).Should().Be(sharedKid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task without_a_signing_key_path_key_management_stays_enabled(string signingKeyPath)
    {
        await using var provider = BuildIdentityServer(signingKeyPath);

        var options = provider.GetRequiredService<IdentityServerOptions>();

        options.KeyManagement.Enabled.Should().BeTrue();
        options.IssuerUri.Should().Be("http://localhost:5000");
    }

    // Compose starts the monolith and identity-service together with an empty shared volume: both
    // find no shared key and race to seed it from their own tempkey.jwk. The loser's File.Copy fails
    // because the file now exists; it must fall back to the winner's key instead of crashing.
    [Fact]
    public async Task hosts_racing_to_seed_the_shared_key_all_start_and_agree_on_the_winner_s_key()
    {
        const int hostCount = 8;
        var contentRoots = Enumerable.Range(0, hostCount).Select(i => Path.Combine(_directory, $"host{i}")).ToList();
        var ownKids = new List<string>();
        foreach (var contentRoot in contentRoots)
        {
            await using var own = BuildIdentityServer(null, contentRoot);
            ownKids.Add(await SigningKeyIdOf(own));
        }
        ownKids.Should().OnlyHaveUniqueItems("every host starts with its own key");
        var shared = Path.Combine(_directory, "shared", "tempkey.jwk");

        using var startTogether = new Barrier(hostCount);
        var hosts = await Task.WhenAll(
            contentRoots.Select(contentRoot =>
                Task.Run(() =>
                {
                    startTogether.SignalAndWait();
                    return BuildIdentityServer(shared, contentRoot);
                })
            )
        );

        var sharedKids = new List<string>();
        foreach (var host in hosts)
            await using (host)
                sharedKids.Add(await SigningKeyIdOf(host));

        sharedKids.Distinct().Should().ContainSingle("all hosts must end up signing with the one shared key");
        ownKids.Should().Contain(sharedKids[0], "the shared key is one of the hosts' pre-existing keys");
        contentRoots
            .Select(contentRoot => File.ReadAllText(Path.Combine(contentRoot, "tempkey.jwk")))
            .Should()
            .Contain(File.ReadAllText(shared));
    }
}
