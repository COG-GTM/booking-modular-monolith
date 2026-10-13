using BuildingBlocks.Constants;
using BuildingBlocks.Jwt;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class JwtApiScopePolicyTests
{
    [Fact]
    public void policy_constant_keeps_the_name_modules_still_reference_via_nameof()
    {
        IdentityConstant.Policy.ApiScope.Should().Be("ApiScope");
    }

    [Fact]
    public void add_jwt_registers_the_api_scope_policy_under_the_shared_constant()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Authority"] = "https://identity.example",
                    ["Jwt:Audience"] = "booking-modular-monolith",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);

        services.AddJwt();

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        var policy = options.GetPolicy(IdentityConstant.Policy.ApiScope);

        policy.Should().NotBeNull();
        policy!.AuthenticationSchemes.Should().Contain(JwtBearerDefaults.AuthenticationScheme);
        policy.Requirements.Should().ContainSingle(r => r is DenyAnonymousAuthorizationRequirement);
        policy
            .Requirements.OfType<ClaimsAuthorizationRequirement>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<ClaimsAuthorizationRequirement>(r =>
                r.ClaimType == "scope" && r.AllowedValues!.Contains("booking-modular-monolith")
            );
        options.GetPolicy(IdentityConstant.Role.Admin).Should().NotBeNull();
        options.GetPolicy(IdentityConstant.Role.User).Should().NotBeNull();
    }
}
