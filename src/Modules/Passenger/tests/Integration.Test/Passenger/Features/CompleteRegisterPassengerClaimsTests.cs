using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Passenger.Data;
using Passenger.Passengers.Enums;
using Passenger.Passengers.ValueObjects;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public class CompleteRegisterPassengerClaimsTests : PassengerIntegrationTestBase
{
    private const string CompleteRegistrationRoute = "api/v1.0/passenger/complete-registration";

    public CompleteRegisterPassengerClaimsTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_complete_registration_when_user_is_identified_by_sub_claim()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var passenger = CreatePassenger(userId, "123456789");

        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 30);

        var client = CreateClientWithClaims(
            new Dictionary<string, object> { { JwtRegisteredClaimNames.Sub, userId.ToString() } }
        );

        // Act
        var result = await client.PostAsJsonAsync(CompleteRegistrationRoute, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK, await result.Content.ReadAsStringAsync());

        var stored = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(passenger.Id);
        stored!.PassengerType.Should().Be(PassengerType.Male);
        stored.Age!.Value.Should().Be(30);
    }

    [Fact]
    public async Task should_reject_complete_registration_when_token_has_no_user_identifier()
    {
        // Arrange
        var passenger = CreatePassenger(Guid.CreateVersion7(), "123456789");

        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 30);

        var client = CreateClientWithClaims(new Dictionary<string, object>());

        // Act
        var result = await client.PostAsJsonAsync(CompleteRegistrationRoute, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stored = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(passenger.Id);
        stored!.PassengerType.Should().Be(PassengerType.Unknown);
        stored.Age.Should().BeNull();
    }

    [Fact]
    public async Task should_reject_complete_registration_when_user_identifier_is_not_a_guid()
    {
        // Arrange
        var passenger = CreatePassenger(Guid.CreateVersion7(), "123456789");

        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 30);

        var client = CreateClientWithClaims(
            new Dictionary<string, object> { { ClaimTypes.NameIdentifier, "not-a-guid" } }
        );

        // Act
        var result = await client.PostAsJsonAsync(CompleteRegistrationRoute, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stored = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(passenger.Id);
        stored!.PassengerType.Should().Be(PassengerType.Unknown);
        stored.Age.Should().BeNull();
    }

    [Fact]
    public async Task should_not_complete_registration_for_legacy_passenger_without_owner()
    {
        // Arrange
        var passenger = CreatePassenger(Guid.Empty, "123456789");

        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 30);

        var client = CreateClientWithClaims(
            new Dictionary<string, object> { { ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString() } }
        );

        // Act
        var result = await client.PostAsJsonAsync(CompleteRegistrationRoute, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stored = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(passenger.Id);
        stored!.PassengerType.Should().Be(PassengerType.Unknown);
        stored.Age.Should().BeNull();
    }

    private static global::Passenger.Passengers.Models.Passenger CreatePassenger(Guid userId, string passportNumber)
    {
        return global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            userId,
            Name.Of("Sam"),
            PassportNumber.Of(passportNumber)
        );
    }

    private HttpClient CreateClientWithClaims(Dictionary<string, object> userClaims)
    {
        var claims = new Dictionary<string, object>
        {
            { ClaimTypes.Name, "test@sample.com" },
            { ClaimTypes.Role, "admin" },
            { "scope", "flight-api" },
        };

        foreach (var claim in userClaims)
        {
            claims[claim.Key] = claim.Value;
        }

        var httpClient = Fixture.HttpClient;
        httpClient.SetFakeBearerToken(claims);

        return httpClient;
    }
}
