using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Data.Seed;
using Flight.Seats.Enums;
using Flight.Seats.Features.CreatingSeat.V1;
using FluentAssertions;
using Xunit;

namespace Integration.Test.Seat.Features;

public class CreateSeatTests : FlightIntegrationTestBase
{
    private const string CreateSeatRoute = "/api/v1/flight/seat";

    public CreateSeatTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task admin_should_create_a_seat()
    {
        var request = CreateSeatRequest("20A");
        using var client = Fixture.HttpClient;

        var response = await client.PostAsJsonAsync(CreateSeatRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task user_should_not_create_a_seat()
    {
        var request = CreateSeatRequest("20B");
        using var client = Fixture.CreateHttpClient("user");

        var response = await client.PostAsJsonAsync(CreateSeatRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task admin_should_get_conflict_when_creating_an_existing_seat_number()
    {
        var existingSeat = InitialData.Seats.First();
        var request = new CreateSeatRequestDto(
            existingSeat.SeatNumber.Value,
            SeatType.Middle,
            SeatClass.Economy,
            existingSeat.FlightId.Value
        );
        using var client = Fixture.HttpClient;

        var response = await client.PostAsJsonAsync(CreateSeatRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private static CreateSeatRequestDto CreateSeatRequest(string seatNumber)
    {
        return new CreateSeatRequestDto(
            seatNumber,
            SeatType.Middle,
            SeatClass.Economy,
            InitialData.Flights.First().Id.Value
        );
    }
}
