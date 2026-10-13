using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.TestBase;
using Contracts.Grpc.Flight;
using Flight.Data;
using Flight.Data.Seed;
using FluentAssertions;
using Grpc.Net.Client;
using Xunit;
using Xunit.Abstractions;

namespace Flight.Host.Test;

// Boots Flight.Host as its own process and calls it over the network: proves the service runs
// standalone (own migrations + seed) and that REST and the shared-contract gRPC client work
// cross-process.
public class FlightHostCrossProcessTests : FlightHostTestBase
{
    private readonly ITestOutputHelper _output;

    public FlightHostCrossProcessTests(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture,
        ITestOutputHelper output
    )
        : base(integrationTestFixture)
    {
        _output = output;
    }

    [Fact]
    public async Task standalone_process_should_serve_rest_and_grpc_on_separate_ports()
    {
        await using var host = await FlightHostProcess.StartAsync(Fixture.Configuration);

        try
        {
            using var http = new HttpClient { BaseAddress = host.HttpAddress };

            (await http.GetStringAsync("/")).Should().Be("Flight-Service");

            var unauthenticated = await http.PostAsJsonAsync("/api/v1.0/flight", new { });
            unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            using var channel = GrpcChannel.ForAddress(host.GrpcAddress);
            var client = new FlightGrpcService.FlightGrpcServiceClient(channel);
            var seededFlight = InitialData.Flights.First();
            var flightId = seededFlight.Id.Value.ToString();

            var flight = await client.GetByIdAsync(new GetByIdRequest { Id = flightId });
            flight.FlightDto.Id.Should().Be(flightId);
            flight.FlightDto.FlightNumber.Should().Be(seededFlight.FlightNumber.Value);

            var seats = await client.GetAvailableSeatsAsync(new GetAvailableSeatsRequest { FlightId = flightId });
            seats.SeatDtos.Should().NotBeEmpty();
            seats.SeatDtos.Should().OnlyContain(s => s.FlightId == flightId);

            var seatToReserve = seats.SeatDtos.First();
            var reserved = await client.ReserveSeatAsync(
                new ReserveSeatRequest { FlightId = flightId, SeatNumber = seatToReserve.SeatNumber }
            );
            reserved.Id.Should().Be(seatToReserve.Id);
        }
        finally
        {
            _output.WriteLine(host.Output);
        }
    }
}
