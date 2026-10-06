using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Xunit;

namespace Integration.Test.Identity.Features;

public class TokenEndpointTests : IdentityIntegrationTestBase
{
    private const string TokenEndpoint = "/connect/token";
    private const string SeededUserName = "samh";
    private const string SeededPassword = "Admin@123456";

    public TokenEndpointTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_reject_the_previously_hardcoded_client_secret()
    {
        var response = await RequestTokenAsync(clientSecret: "secret", scope: "booking-modular-monolith role");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(response)).Should().Be("invalid_client");
    }

    [Fact]
    public async Task should_issue_token_with_configured_client_secret()
    {
        var response = await RequestTokenAsync(
            clientSecret: ConfiguredClientSecret,
            scope: "openid profile role booking-modular-monolith");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        payload.RootElement.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        payload.RootElement.GetProperty("scope").GetString()!.Split(' ')
            .Should().BeEquivalentTo("openid", "profile", "role", "booking-modular-monolith");
    }

    [Theory]
    [InlineData("flight-api")]
    [InlineData("passenger-api")]
    [InlineData("booking-api")]
    [InlineData("identity-api")]
    public async Task should_not_grant_scopes_the_client_does_not_need(string scope)
    {
        var response = await RequestTokenAsync(clientSecret: ConfiguredClientSecret, scope: scope);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(response)).Should().Be("invalid_scope");
    }

    private string ConfiguredClientSecret => Fixture.Configuration["AuthOptions:ClientSecret"]!;

    private Task<HttpResponseMessage> RequestTokenAsync(string clientSecret, string scope)
    {
        var form = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "client",
                ["client_secret"] = clientSecret,
                ["username"] = SeededUserName,
                ["password"] = SeededPassword,
                ["scope"] = scope,
            });

        return Fixture.HttpClient.PostAsync(TokenEndpoint, form);
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return payload.RootElement.GetProperty("error").GetString();
    }
}
