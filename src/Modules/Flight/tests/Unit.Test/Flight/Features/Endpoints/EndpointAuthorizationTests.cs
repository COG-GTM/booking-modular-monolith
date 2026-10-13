using BuildingBlocks.Constants;
using BuildingBlocks.Web;
using Duende.IdentityServer.EntityFramework.Entities;
using FluentAssertions;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Unit.Test.Flight.Features.Endpoints;

using global::Flight.Aircrafts.Features.CreatingAircraft.V1;
using global::Flight.Airports.Features.CreatingAirport.V1;
using global::Flight.Flights.Features.CreatingFlight.V1;
using global::Flight.Flights.Features.DeletingFlight.V1;
using global::Flight.Flights.Features.GettingAvailableFlights.V1;
using global::Flight.Flights.Features.GettingFlightById.V1;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using global::Flight.Seats.Features.CreatingSeat.V1;
using global::Flight.Seats.Features.GettingAvailableSeats.V1;
using global::Flight.Seats.Features.ReservingSeat.V1;

public class EndpointAuthorizationTests
{
    [Theory]
    [InlineData(typeof(CreateFlightEndpoint))]
    [InlineData(typeof(UpdateFlightEndpoint))]
    [InlineData(typeof(DeleteFlightEndpoint))]
    [InlineData(typeof(CreateAircraftEndpoint))]
    [InlineData(typeof(CreateAirportEndpoint))]
    [InlineData(typeof(CreateSeatEndpoint))]
    public void administrative_endpoints_should_require_api_scope_and_admin_role_policies(Type endpointType)
    {
        // Act
        var policies = GetAuthorizationPolicies(endpointType);

        // Assert
        policies.Should().BeEquivalentTo(new[] { nameof(ApiScope), IdentityConstant.Role.Admin });
    }

    [Theory]
    [InlineData(typeof(GetFlightByIdEndpoint))]
    [InlineData(typeof(GetAvailableFlightsEndpoint))]
    [InlineData(typeof(GetAvailableSeatsEndpoint))]
    [InlineData(typeof(ReserveSeatEndpoint))]
    public void read_and_self_service_endpoints_should_only_require_api_scope_policy(Type endpointType)
    {
        // Act
        var policies = GetAuthorizationPolicies(endpointType);

        // Assert
        policies.Should().BeEquivalentTo(new[] { nameof(ApiScope) });
        policies.Should().NotContain(IdentityConstant.Role.Admin);
    }

    [Theory]
    [InlineData(typeof(CreateFlightEndpoint))]
    [InlineData(typeof(UpdateFlightEndpoint))]
    [InlineData(typeof(DeleteFlightEndpoint))]
    [InlineData(typeof(CreateAircraftEndpoint))]
    [InlineData(typeof(CreateAirportEndpoint))]
    [InlineData(typeof(CreateSeatEndpoint))]
    public void administrative_endpoints_should_not_allow_anonymous_access(Type endpointType)
    {
        // Act
        var endpoint = MapEndpoint(endpointType);

        // Assert
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Should().NotBeEmpty();
    }

    private static IReadOnlyList<string> GetAuthorizationPolicies(Type endpointType)
    {
        return MapEndpoint(endpointType)
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(x => x.Policy)
            .Where(x => !string.IsNullOrEmpty(x))
            .Cast<string>()
            .ToList();
    }

    private static Endpoint MapEndpoint(Type endpointType)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddApiVersioning();
        builder.Services.AddSingleton(Substitute.For<IMediator>());
        builder.Services.AddSingleton(Substitute.For<IMapper>());
        var app = builder.Build();

        var endpoint = (IMinimalEndpoint)Activator.CreateInstance(endpointType)!;
        endpoint.MapEndpoint(app);

        return ((IEndpointRouteBuilder)app).DataSources.SelectMany(x => x.Endpoints).Single();
    }
}
