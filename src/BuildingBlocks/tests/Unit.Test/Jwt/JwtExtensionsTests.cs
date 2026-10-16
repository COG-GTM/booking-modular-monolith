using System.Security.Claims;
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

namespace BuildingBlocks.Unit.Test.JwtTests;

public class JwtExtensionsTests
{
    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddJwt();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void add_jwt_binds_bearer_options_from_configuration()
    {
        using var provider = BuildProvider(
            new()
            {
                ["Jwt:Authority"] = "https://identity.example",
                ["Jwt:Audience"] = "booking-audience",
                ["Jwt:MetadataAddress"] = "http://identity:8080/.well-known",
            }
        );

        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Authority.Should().Be("https://identity.example");
        options.Audience.Should().Be("booking-audience");
        options.RequireHttpsMetadata.Should().BeFalse();
        options.MapInboundClaims.Should().BeFalse();
        options.MetadataAddress.Should().Be("http://identity:8080/.well-known");

        var tvp = options.TokenValidationParameters;
        tvp.ValidIssuers.Should().ContainSingle().Which.Should().Be("https://identity.example");
        tvp.ValidAudiences.Should().ContainSingle().Which.Should().Be("booking-audience");
        tvp.ClockSkew.Should().Be(TimeSpan.FromSeconds(2));
        tvp.NameClaimType.Should().Be("name");
        tvp.RoleClaimType.Should().Be("role");
        tvp.ValidateIssuerSigningKey.Should().BeTrue();
        tvp.ValidateIssuer.Should().BeTrue();
        tvp.ValidateAudience.Should().BeTrue();
        tvp.ValidateLifetime.Should().BeTrue();
    }

    [Fact]
    public void metadata_address_defaults_to_the_authority_discovery_document_when_not_configured()
    {
        using var provider = BuildProvider(
            new() { ["Jwt:Authority"] = "https://identity.example", ["Jwt:Audience"] = "booking-audience" }
        );

        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.MetadataAddress.Should().Be("https://identity.example/.well-known/openid-configuration");
    }

    [Fact]
    public void role_policies_exist_with_roles_requirements()
    {
        using var provider = BuildProvider(
            new() { ["Jwt:Authority"] = "https://identity.example", ["Jwt:Audience"] = "booking-audience" }
        );

        var authorizationOptions = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value;

        var admin = authorizationOptions.GetPolicy(IdentityConstant.Role.Admin);
        admin!
            .Requirements.OfType<RolesAuthorizationRequirement>()
            .Should()
            .ContainSingle()
            .Which.AllowedRoles.Should()
            .Contain("admin");

        var user = authorizationOptions.GetPolicy(IdentityConstant.Role.User);
        user!
            .Requirements.OfType<RolesAuthorizationRequirement>()
            .Should()
            .ContainSingle()
            .Which.AllowedRoles.Should()
            .Contain("user");
    }

    [Fact]
    public async Task api_scope_policy_succeeds_for_authenticated_principal_with_the_scope_claim()
    {
        using var provider = BuildProvider(
            new() { ["Jwt:Authority"] = "https://identity.example", ["Jwt:Audience"] = "booking-audience" }
        );
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var scoped = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("scope", "booking-audience")], authenticationType: "Bearer")
        );
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var missingScope = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim("sub", "1")], authenticationType: "Bearer")
        );

        (await authorization.AuthorizeAsync(scoped, null, IdentityConstant.Policy.ApiScope))
            .Succeeded.Should()
            .BeTrue();
        (await authorization.AuthorizeAsync(anonymous, null, IdentityConstant.Policy.ApiScope))
            .Succeeded.Should()
            .BeFalse();
        (await authorization.AuthorizeAsync(missingScope, null, IdentityConstant.Policy.ApiScope))
            .Succeeded.Should()
            .BeFalse();
    }

    [Fact]
    public async Task admin_policy_succeeds_for_admin_role_and_fails_for_user_role()
    {
        using var provider = BuildProvider(
            new() { ["Jwt:Authority"] = "https://identity.example", ["Jwt:Audience"] = "booking-audience" }
        );
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var admin = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("name", "root"), new Claim("role", "admin")],
                authenticationType: "Bearer",
                nameType: "name",
                roleType: "role"
            )
        );
        var user = new ClaimsPrincipal(
            new ClaimsIdentity(
                [new Claim("name", "bob"), new Claim("role", "user")],
                authenticationType: "Bearer",
                nameType: "name",
                roleType: "role"
            )
        );

        (await authorization.AuthorizeAsync(admin, null, IdentityConstant.Role.Admin)).Succeeded.Should().BeTrue();
        (await authorization.AuthorizeAsync(user, null, IdentityConstant.Role.Admin)).Succeeded.Should().BeFalse();
    }
}
