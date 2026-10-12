using System.Net;
using System.Security.Claims;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Integration.Test.Seat.Features;

public class FlightGrpcScopeAuthorizationTests : FlightIntegrationTestBase
{
    public FlightGrpcScopeAuthorizationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_reject_anonymous_get_by_id_from_grpc_service()
    {
        // Arrange
        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.AnonymousChannel);

        // Act
        var act = async () =>
            await flightGrpcClient.GetByIdAsync(new GetByIdRequest { Id = Guid.NewGuid().ToString() });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task should_reject_authenticated_caller_without_flight_api_scope_from_grpc_service()
    {
        // Arrange
        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(CreateChannelWithScope("booking-api"));

        // Act
        var act = async () =>
            await flightGrpcClient.GetAvailableSeatsAsync(
                new GetAvailableSeatsRequest { FlightId = Guid.NewGuid().ToString() }
            );

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    private GrpcChannel CreateChannelWithScope(string scope)
    {
        var claims = new Dictionary<string, object> { { ClaimTypes.Name, "test@sample.com" }, { "scope", scope } };

        var httpClient = Fixture.HttpClient;
        httpClient.SetFakeBearerToken(claims);

        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }
}
