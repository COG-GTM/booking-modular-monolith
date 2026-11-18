using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Constants;
using Identity.Identity.Models;
using IdentityModel;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace Integration.Test.Identity.Configurations;

public class AccessTokenRoleTests : IdentityIntegrationTestBase
{
    public AccessTokenRoleTests(TestWriteFixture<Program, IdentityContext> fixture)
        : base(fixture) { }

    [Theory]
    [InlineData(IdentityConstant.Role.Admin)]
    [InlineData(IdentityConstant.Role.User)]
    public async Task should_issue_access_token_with_assigned_role_without_requesting_role_scope(string role)
    {
        var username = $"token-test-{Guid.NewGuid():N}";
        var password = $"Test-{Guid.NewGuid():N}";
        var user = new User
        {
            UserName = username,
            Email = $"{username}@example.test",
            FirstName = "Token",
            LastName = "Tester",
            PassPortNumber = "00000001",
        };
        await using var scope = Fixture.ServiceProvider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        (await userManager.CreateAsync(user, password)).Succeeded.Should().BeTrue();
        (await userManager.AddToRoleAsync(user, role)).Succeeded.Should().BeTrue();

        using var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;
        using var request = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "client",
                ["client_secret"] = "secret",
                ["username"] = username,
                ["password"] = password,
                ["scope"] = Constants.StandardScopes.BookingModularMonolith,
            }
        );

        using var response = await client.PostAsync("/connect/token", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = payload.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();
        var token = new JsonWebToken(accessToken!);
        token.Claims.Where(claim => claim.Type == JwtClaimTypes.Role).Select(claim => claim.Value).Should().Equal(role);
    }
}
