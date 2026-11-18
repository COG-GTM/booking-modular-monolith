using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Xunit;

namespace Integration.Test.Identity.Features;

public class SigningKeyTests : IdentityIntegrationTestBase
{
    // Key id of the RSA key that used to be committed as src/Api/src/tempkey.jwk.
    private const string CompromisedKeyId = "5FDEBAED6A154EEFD68A1B54BE25EDC1";

    public SigningKeyTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task jwks_should_not_publish_the_previously_committed_signing_key()
    {
        // Act
        var jwks = await Fixture.HttpClient.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration/jwks");

        // Assert
        var keyIds = jwks.GetProperty("keys").EnumerateArray()
            .Select(key => key.GetProperty("kid").GetString())
            .ToList();

        keyIds.Should().NotBeEmpty();
        keyIds.Should().NotContain(CompromisedKeyId);
    }
}
