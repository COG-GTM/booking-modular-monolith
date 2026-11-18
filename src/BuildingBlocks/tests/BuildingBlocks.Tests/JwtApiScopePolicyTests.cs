using BuildingBlocks.Constants;
using BuildingBlocks.Jwt;
using Duende.IdentityServer.EntityFramework.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Tests;

public class JwtApiScopePolicyTests
{
    [Fact]
    public void api_scope_policy_name_matches_duende_entity_name()
    {
        IdentityConstant.Policy.ApiScope.Should().Be(nameof(ApiScope));
    }

    [Fact]
    public async Task add_jwt_registers_the_authenticated_audience_scope_policy()
    {
        const string audience = "booking-api";
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Jwt:Authority"] = "https://identity.example.test",
                        ["Jwt:Audience"] = audience,
                    }
                )
                .Build()
        );
        services.AddJwt();
        using var provider = services.BuildServiceProvider();

        var policy = await provider
            .GetRequiredService<IAuthorizationPolicyProvider>()
            .GetPolicyAsync(IdentityConstant.Policy.ApiScope);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(JwtBearerDefaults.AuthenticationScheme);
        policy.Requirements.Should().ContainSingle(requirement => requirement is DenyAnonymousAuthorizationRequirement);
        var scopeRequirement = policy
            .Requirements.OfType<ClaimsAuthorizationRequirement>()
            .Should()
            .ContainSingle()
            .Which;
        scopeRequirement.ClaimType.Should().Be("scope");
        scopeRequirement.AllowedValues.Should().Equal(audience);
    }
}
