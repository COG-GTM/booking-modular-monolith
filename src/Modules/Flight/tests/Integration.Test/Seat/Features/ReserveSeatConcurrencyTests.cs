using BuildingBlocks.TestBase;
using Flight;
using Flight.Data;
using FluentAssertions;
using Grpc.Core;
using Integration.Test.Fakes;
using Xunit;

namespace Integration.Test.Seat.Features;

public class ReserveSeatConcurrencyTests : FlightIntegrationTestBase
{
    private const int ParallelRequests = 20;

    public ReserveSeatConcurrencyTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory) : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task parallel_reserve_seat_calls_should_reserve_the_seat_only_once()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        var request = new ReserveSeatRequest
        {
            FlightId = seatCommand.FlightId.ToString(),
            SeatNumber = seatCommand.SeatNumber
        };

        using var start = new ManualResetEventSlim(false);

        // Act
        var calls = Enumerable.Range(0, ParallelRequests)
            .Select(_ => Task.Run(async () =>
            {
                start.Wait();
                try
                {
                    await flightGrpcClient.ReserveSeatAsync(request);
                    return (StatusCode?)null;
                }
                catch (RpcException ex)
                {
                    return ex.StatusCode;
                }
            }))
            .ToArray();

        start.Set();
        var results = await Task.WhenAll(calls);

        // Assert
        results.Count(r => r is null).Should().Be(1);
        results.Where(r => r is not null).Should().AllBeEquivalentTo(StatusCode.Aborted);
    }

    [Fact]
    public async Task reserve_seat_for_already_reserved_seat_should_return_aborted()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        var flightGrpcClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        var request = new ReserveSeatRequest
        {
            FlightId = seatCommand.FlightId.ToString(),
            SeatNumber = seatCommand.SeatNumber
        };

        await flightGrpcClient.ReserveSeatAsync(request);

        // Act
        var act = async () => await flightGrpcClient.ReserveSeatAsync(request);

        // Assert
        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Aborted);
    }

    [Fact]
    public async Task anonymous_grpc_calls_should_be_rejected()
    {
        // Arrange
        var flightCommand = new FakeCreateFlightCommand().Generate();
        await Fixture.SendAsync(flightCommand);

        var seatCommand = new FakeCreateSeatCommand(flightCommand.Id).Generate();
        await Fixture.SendAsync(seatCommand);

        var anonymousClient = new FlightGrpcService.FlightGrpcServiceClient(Fixture.AnonymousChannel);

        // Act
        var reserve = async () => await anonymousClient.ReserveSeatAsync(new ReserveSeatRequest
        {
            FlightId = seatCommand.FlightId.ToString(),
            SeatNumber = seatCommand.SeatNumber
        });
        var available = async () => await anonymousClient.GetAvailableSeatsAsync(
            new GetAvailableSeatsRequest { FlightId = flightCommand.Id.ToString() });

        // Assert
        (await reserve.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
        (await available.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }
}
