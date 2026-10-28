using BuildingBlocks.Constants;
using BuildingBlocks.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class JwtExtensionsTests
{
    private const string Authority = "http://identity.test";
    private const string Audience = "flight-api";

    [Fact]
    public void RegistersApiScopePolicyRequiringTheConfiguredAudienceScope()
    {
        using var provider = BuildProvider();

        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("ApiScope");

        Assert.NotNull(policy);
        Assert.Equal([JwtBearerDefaults.AuthenticationScheme], policy.AuthenticationSchemes);
        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
        var scope = Assert.Single(policy.Requirements.OfType<ClaimsAuthorizationRequirement>());
        Assert.Equal("scope", scope.ClaimType);
        Assert.Equal([Audience], scope.AllowedValues!);
    }

    [Theory]
    [InlineData(IdentityConstant.Role.Admin)]
    [InlineData(IdentityConstant.Role.User)]
    public void RegistersRolePolicies(string role)
    {
        using var provider = BuildProvider();

        var policy = provider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy(role);

        Assert.NotNull(policy);
        Assert.Equal([JwtBearerDefaults.AuthenticationScheme], policy.AuthenticationSchemes);
        var roles = Assert.Single(policy.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Equal([role], roles.AllowedRoles);
    }

    [Fact]
    public void ConfiguresJwtBearerFromTheJwtSection()
    {
        using var provider = BuildProvider();

        var options = provider
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        Assert.Equal(Authority, options.Authority);
        Assert.Equal(Audience, options.Audience);
        Assert.False(options.RequireHttpsMetadata);
        Assert.False(options.MapInboundClaims);
        Assert.Equal([Authority], options.TokenValidationParameters.ValidIssuers);
        Assert.Equal([Audience], options.TokenValidationParameters.ValidAudiences);
        Assert.Equal(TimeSpan.FromSeconds(2), options.TokenValidationParameters.ClockSkew);
        Assert.Equal("name", options.TokenValidationParameters.NameClaimType);
        Assert.Equal("role", options.TokenValidationParameters.RoleClaimType);
    }

    [Fact]
    public void JwtPackageDoesNotDependOnIdentityServer()
    {
        var references = typeof(JwtExtensions).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();

        Assert.DoesNotContain(references, name => name!.StartsWith("Duende", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["Jwt:Authority"] = Authority, ["Jwt:Audience"] = Audience }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        services.AddJwt();

        return services.BuildServiceProvider();
    }
}
