using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Xunit;

namespace Integration.Test.Seat.Features;

public class FlightGrpcAuthorizationTests : FlightIntegrationTestBase
{
    public FlightGrpcAuthorizationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_reject_anonymous_reserve_seat_from_grpc_service()
    {
        // Arrange
        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.AnonymousChannel);

        // Act
        var act = async () => await flightGrpcClient.ReserveSeatAsync(new ReserveSeatRequest
        {
            FlightId = Guid.NewGuid().ToString(),
            SeatNumber = "1A"
        });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task should_reject_anonymous_get_available_seats_from_grpc_service()
    {
        // Arrange
        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.AnonymousChannel);

        // Act
        var act = async () => await flightGrpcClient.GetAvailableSeatsAsync(new GetAvailableSeatsRequest
        {
            FlightId = Guid.NewGuid().ToString()
        });

        // Assert
        var exception = await act.Should().ThrowAsync<RpcException>();
        exception.Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }
}
