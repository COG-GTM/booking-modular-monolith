using System.Net;
using System.Security.Claims;
using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Grpc.Net.Client;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Integration.Test.GrpcServer;

public class FlightGrpcAuthorizationTests : FlightIntegrationTestBase
{
    public FlightGrpcAuthorizationTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_reject_anonymous_get_flight_by_id_from_grpc_service()
    {
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var client = new FlightGrpcService.FlightGrpcServiceClient(Fixture.AnonymousChannel);

        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = flightCommand.Id.ToString() });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task should_deny_get_flight_by_id_for_token_without_api_scope()
    {
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var client = new FlightGrpcService.FlightGrpcServiceClient(CreateChannelWithoutApiScope());

        var act = async () => await client.GetByIdAsync(new GetByIdRequest { Id = flightCommand.Id.ToString() });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task should_deny_get_available_seats_for_token_without_api_scope()
    {
        var flightCommand = new FakeCreateFlightMongoCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var client = new FlightGrpcService.FlightGrpcServiceClient(CreateChannelWithoutApiScope());

        var act = async () =>
            await client.GetAvailableSeatsAsync(
                new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() }
            );

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);
    }

    [Fact]
    public async Task should_deny_reserve_seat_for_token_without_api_scope_and_leave_seat_unchanged()
    {
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        var client = new FlightGrpcService.FlightGrpcServiceClient(CreateChannelWithoutApiScope());

        var act = async () =>
            await client.ReserveSeatAsync(
                new ReserveSeatRequest
                {
                    FlightId = seatCommand.FlightId.ToString(),
                    SeatNumber = seatCommand.SeatNumber,
                }
            );

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.PermissionDenied);

        var isDeleted = await Fixture.ExecuteDbContextAsync(db =>
            db.Seats.IgnoreQueryFilters()
                .Where(x => x.SeatNumber.Value == seatCommand.SeatNumber && x.FlightId == seatCommand.FlightId)
                .Select(x => x.IsDeleted)
                .SingleAsync()
        );

        isDeleted.Should().BeFalse();
    }

    private GrpcChannel CreateChannelWithoutApiScope()
    {
        var httpClient = Fixture.HttpClient;
        httpClient.SetFakeBearerToken(
            new Dictionary<string, object>
            {
                { ClaimTypes.Name, "test@sample.com" },
                { ClaimTypes.Role, "admin" },
                { "scope", "passenger-api" },
            }
        );

        return GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient });
    }
}
