using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using Flight.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Seat.Features;

public class CreateSeatEndpointMetadataTests : FlightIntegrationTestBase
{
    public CreateSeatEndpointMetadataTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public void endpoint_should_require_api_scope_and_admin_policies()
    {
        var endpoint = GetCreateSeatEndpoint();

        endpoint
            .Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(x => x.Policy)
            .Should()
            .Contain("ApiScope")
            .And.Contain(IdentityConstant.Role.Admin);
        endpoint.Metadata.GetMetadata<IAllowAnonymous>().Should().BeNull();
    }

    [Theory]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status409Conflict)]
    public void endpoint_should_document_problem_responses(int statusCode)
    {
        var endpoint = GetCreateSeatEndpoint();

        var response = endpoint
            .Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()
            .Single(x => x.StatusCode == statusCode);

        response.Type.Should().Be(typeof(Microsoft.AspNetCore.Mvc.ProblemDetails));
        response.ContentTypes.Should().Contain("application/problem+json");
    }

    private Endpoint GetCreateSeatEndpoint()
    {
        var endpoints = Fixture.ServiceProvider.GetRequiredService<EndpointDataSource>();
        return endpoints.Endpoints.Single(x =>
            x.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "CreateSeat"
        );
    }
}
