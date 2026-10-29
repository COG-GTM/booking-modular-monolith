using System.Net;
using System.Text.Json;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Xunit;

namespace Host.Test;

public class IdentityServerTests : IdentityHostTestBase
{
    public IdentityServerTests(TestWriteFixture<Identity.Host.Program, IdentityContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task issues_access_token_and_serves_discovery_document()
    {
        using var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;

        var tokenResponse = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["username"] = "samh",
                    ["password"] = "Admin@123456",
                    ["client_id"] = "client",
                    ["client_secret"] = "secret",
                    ["scope"] = "openid booking-modular-monolith",
                }
            )
        );

        tokenResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokenDocument = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var accessToken = tokenDocument.RootElement.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrWhiteSpace();

        var payload = DecodeJwtPayload(accessToken!);
        payload.GetProperty("iss").GetString().Should().Be("http://localhost:5103");
        var audience = payload.GetProperty("aud");
        var audiences =
            audience.ValueKind == JsonValueKind.Array
                ? audience.EnumerateArray().Select(value => value.GetString())
                : [audience.GetString()];
        audiences.Should().Contain("booking-modular-monolith");

        var discoveryResponse = await client.GetAsync("/.well-known/openid-configuration");
        discoveryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static JsonElement DecodeJwtPayload(string token)
    {
        var payloadPart = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payloadPart = payloadPart.PadRight(payloadPart.Length + ((4 - payloadPart.Length % 4) % 4), '=');
        using var document = JsonDocument.Parse(Convert.FromBase64String(payloadPart));
        return document.RootElement.Clone();
    }
}
