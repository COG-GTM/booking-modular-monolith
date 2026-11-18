using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Xunit;

namespace Integration.Test.Identity.Features;

public class TokenUsernameEnumerationTests : IdentityIntegrationTestBase
{
    private const string KnownUserName = "samh";
    private const string KnownUserPassword = "Admin@123456";

    public TokenUsernameEnumerationTests(
        TestWriteFixture<Program, IdentityContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task unknown_username_should_get_same_response_as_wrong_password()
    {
        using var unknownUser = await RequestTokenAsync("no-such-user-bfebd5f2", "WrongPassword1");
        using var wrongPassword = await RequestTokenAsync(KnownUserName, "WrongPassword1");

        var unknownUserBody = await unknownUser.Content.ReadAsStringAsync();
        var wrongPasswordBody = await wrongPassword.Content.ReadAsStringAsync();

        wrongPassword.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        unknownUser.StatusCode.Should().Be(wrongPassword.StatusCode);
        unknownUserBody.Should().Be(wrongPasswordBody);
        unknownUserBody.Should().Contain("Invalid Credentials");
    }

    [Fact]
    public async Task valid_credentials_should_issue_token_without_sign_in_cookie()
    {
        using var response = await RequestTokenAsync(KnownUserName, KnownUserPassword);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("access_token");

        var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values : Enumerable.Empty<string>();
        cookies.Should().NotContain(c => c.StartsWith(".AspNetCore.Identity.Application"));
    }

    private async Task<HttpResponseMessage> RequestTokenAsync(string userName, string password)
    {
        var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;

        return await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "client",
                ["client_secret"] = "secret",
                ["username"] = userName,
                ["password"] = password,
                ["scope"] = "flight-api",
            }));
    }
}