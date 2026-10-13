using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Constants;
using Xunit;

namespace Integration.Test.Identity.Features;

public class AccessTokenTests : IdentityIntegrationTestBase
{
    public AccessTokenTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_issue_access_token_with_role_claim_without_requesting_role_scope()
    {
        // Arrange: seeded admin user (see IdentityTestDataSeeder) via the ROPC client,
        // requesting only the API scope and NOT the optional `role` scope.
        var tokenRequest = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "client",
                ["client_secret"] = "secret",
                ["username"] = "samh",
                ["password"] = "Admin@123456",
                ["scope"] = Constants.StandardScopes.BookingModularMonolith,
            }
        );

        // Act
        var response = await Fixture.AnonymousHttpClient.PostAsync("/connect/token", tokenRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = body.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();

        var claims = DecodeJwtPayload(accessToken!);

        GetValues(claims, "aud").Should().Contain(Constants.StandardScopes.BookingModularMonolith);
        GetValues(claims, "scope").Should().Contain(Constants.StandardScopes.BookingModularMonolith);
        GetValues(claims, "role").Should().Contain(IdentityConstant.Role.Admin);
    }

    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var payload = jwt.Split('.')[1];
        var padded = payload.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    private static IEnumerable<string> GetValues(JsonElement claims, string name)
    {
        if (!claims.TryGetProperty(name, out var element))
        {
            return Enumerable.Empty<string>();
        }

        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(x => x.GetString()!)
            : new[] { element.GetString()! };
    }
}
