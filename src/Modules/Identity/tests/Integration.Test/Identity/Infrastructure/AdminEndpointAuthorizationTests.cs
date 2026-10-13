using BuildingBlocks.Constants;
using BuildingBlocks.Web;
using Duende.IdentityServer.EntityFramework.Entities;
using FluentAssertions;
using Identity.Configurations;
using IdentityModel;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

using global::Identity.Identity.Constants;
using global::Identity.Identity.Features.RegisteringNewUser.V1;

public class AdminEndpointAuthorizationTests
{
    [Fact]
    public void register_new_user_endpoint_should_require_api_scope_and_admin_role_policies()
    {
        // Act
        var endpoint = MapEndpoint(new RegisterNewUserEndpoint());
        var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(x => x.Policy).ToList();

        // Assert
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        policies.Should().BeEquivalentTo(new[] { nameof(ApiScope), IdentityConstant.Role.Admin });
    }

    [Fact]
    public void booking_modular_monolith_api_resource_should_emit_role_user_claim()
    {
        // Act
        var apiResource = Config.ApiResources.Single(x => x.Name == Constants.StandardScopes.BookingModularMonolith);

        // Assert
        apiResource.Scopes.Should().Contain(Constants.StandardScopes.BookingModularMonolith);
        apiResource.UserClaims.Should().Contain(JwtClaimTypes.Role);
    }

    [Fact]
    public void role_claim_should_be_emitted_for_the_booking_modular_monolith_audience_only()
    {
        // Act
        var resourcesWithRoleClaim = Config
            .ApiResources.Where(x => x.UserClaims.Contains(JwtClaimTypes.Role))
            .Select(x => x.Name)
            .ToList();

        // Assert
        resourcesWithRoleClaim.Should().BeEquivalentTo(new[] { Constants.StandardScopes.BookingModularMonolith });
    }

    private static Endpoint MapEndpoint(IMinimalEndpoint minimalEndpoint)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddApiVersioning();
        builder.Services.AddSingleton(Substitute.For<IMediator>());
        builder.Services.AddSingleton(Substitute.For<IMapper>());
        var app = builder.Build();

        minimalEndpoint.MapEndpoint(app);

        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).Single();
    }
}
