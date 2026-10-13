using System.Net;
using System.Text.Json;
using FluentAssertions;
using Identity.Identity.Constants;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Identity.Host.Test;

// Drives the real IdentityServer inside the standalone host: tokens it issues must be verifiable with the
// keys it publishes and must carry the audience the host (and the gateway) validate bearer tokens against.
public class IdentityHostTokenValidationTests(IdentityHostTestFixture fixture) : IdentityHostTestBase(fixture)
{
    private const string RegisterUserPath = "/api/v1.0/identity/register-user";

    [Fact]
    public async Task issued_token_should_validate_against_published_jwks_with_the_configured_audience()
    {
        var configuredAudience = Fixture.Configuration["Jwt:Audience"];
        configuredAudience.Should().Be(Constants.StandardScopes.BookingModularMonolith);

        var accessToken = await RequestAccessTokenAsync(configuredAudience!);

        using var discovery = JsonDocument.Parse(
            await Fixture.HttpClient.GetStringAsync("/.well-known/openid-configuration")
        );
        var jwksPath = new Uri(discovery.RootElement.GetProperty("jwks_uri").GetString()!).PathAndQuery;
        var jwks = new JsonWebKeySet(await Fixture.HttpClient.GetStringAsync(jwksPath));
        jwks.Keys.Should().NotBeEmpty("the host must publish its signing keys");

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            accessToken,
            new TokenValidationParameters
            {
                ValidIssuer = IdentityHostTestFixture.IssuerUri,
                ValidAudience = configuredAudience,
                IssuerSigningKeys = jwks.GetSigningKeys(),
                ValidateLifetime = true,
            }
        );

        result.IsValid.Should().BeTrue(result.Exception?.ToString());
        result.ClaimsIdentity.FindFirst("scope")?.Value.Should().Be(configuredAudience);
    }

    [Fact]
    public async Task issued_token_should_not_carry_an_audience_that_was_not_requested()
    {
        var accessToken = await RequestAccessTokenAsync(Constants.StandardScopes.IdentityApi);

        var token = new JsonWebToken(accessToken);

        token.Audiences.Should().Contain(Constants.StandardScopes.IdentityApi);
        token
            .Audiences.Should()
            .NotContain(
                Constants.StandardScopes.BookingModularMonolith,
                "the gateway audience is only granted when its scope is requested"
            );
    }

    [Fact]
    public async Task register_user_should_reject_anonymous_requests()
    {
        var anonymousClient = Fixture.HttpClient;
        anonymousClient.DefaultRequestHeaders.Authorization = null;

        var response = await anonymousClient.PostAsync(
            RegisterUserPath,
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json")
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public void composed_host_should_route_only_identity_endpoints_and_map_each_probe_once()
    {
        var routes = Fixture
            .ServiceProvider.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/'))
            .ToList();

        routes
            .Where(r => r.StartsWith("/api/", StringComparison.Ordinal))
            .Should()
            .NotBeEmpty()
            .And.OnlyContain(
                r => r.StartsWith("/api/v{version:apiVersion}/identity/", StringComparison.Ordinal),
                "the standalone host must expose the Identity module's endpoints and nothing from other modules"
            );
        routes.Should().ContainSingle(r => r == "/health").And.ContainSingle(r => r == "/alive");
    }

    private async Task<string> RequestAccessTokenAsync(string scope)
    {
        var response = await Fixture.HttpClient.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "client",
                    ["client_secret"] = "secret",
                    ["username"] = IdentityHostTestDataSeeder.UserName,
                    ["password"] = IdentityHostTestDataSeeder.Password,
                    ["scope"] = scope,
                }
            )
        );

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("access_token").GetString()!;
    }
}
