using System.IdentityModel.Tokens.Jwt;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using Duende.IdentityServer.Models;
using Duende.IdentityServer.Services;
using Duende.IdentityServer.Validation;
using FluentAssertions;
using IdentityModel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity;

using global::Identity.Configurations;
using global::Identity.Data;
using global::Identity.Data.Seed;
using global::Identity.Identity.Constants;

public class AccessTokenRoleClaimTests(TestWriteFixture<Program, IdentityContext> fixture)
    : IdentityIntegrationTestBase(fixture)
{
    [Fact]
    public async Task should_issue_an_admin_role_claim_when_only_the_api_scope_is_requested()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var client = Config.Clients.Single();
        var resourceRequest = new ResourceValidationRequest
        {
            Client = client,
            Scopes = [Constants.StandardScopes.BookingModularMonolith],
        };
        var resourceValidator = scope.ServiceProvider.GetRequiredService<IResourceValidator>();
        var resources = await resourceValidator.ValidateRequestedResourcesAsync(resourceRequest);
        resources.Succeeded.Should().BeTrue();

        var adminId = InitialData.Users.First().Id.ToString();
        var grant = new GrantValidationResult(adminId, "pwd");
        var request = new ValidatedTokenRequest
        {
            GrantType = OidcConstants.GrantTypes.Password,
            Subject = grant.Subject,
        };
        request.SetClient(client);
        var tokenRequest = new TokenCreationRequest
        {
            Subject = grant.Subject,
            ValidatedRequest = request,
            ValidatedResources = resources,
        };

        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var accessToken = await tokenService.CreateAccessTokenAsync(tokenRequest);
        var jwt = await tokenService.CreateSecurityTokenAsync(accessToken);

        new JwtSecurityTokenHandler()
            .ReadJwtToken(jwt)
            .Claims.Should()
            .Contain(claim => claim.Type == JwtClaimTypes.Role && claim.Value == IdentityConstant.Role.Admin);
    }
}
