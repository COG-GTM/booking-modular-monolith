using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Routes;
using Flight.Data;
using Flight.Seats.Features.ReservingSeat.V1;
using FluentAssertions;
using Xunit;

namespace EndToEnd.Test.Seat.Features;

public class ReserveSeatPolicyTests : FlightEndToEndTestBase
{
    public ReserveSeatPolicyTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_challenge_reserve_seat_without_credentials()
    {
        using var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;
        var request = new ReserveSeatRequestDto(Guid.NewGuid(), "12A");

        using var response = await client.PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_forbid_reserve_seat_for_admin_without_flight_api_scope()
    {
        using var client = Fixture.CreateHttpClient(IdentityConstant.Role.Admin);
        client.SetFakeBearerToken(
            new Dictionary<string, object>
            {
                { ClaimTypes.Name, "test@sample.com" },
                { ClaimTypes.Role, IdentityConstant.Role.Admin },
                { "scope", "passenger-api" },
            }
        );
        var request = new ReserveSeatRequestDto(Guid.NewGuid(), "12A");

        using var response = await client.PostAsJsonAsync(ApiRoutes.Flight.ReserveSeat, request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
