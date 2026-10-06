using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Passenger.Data;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.ValueObjects;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public class CompleteRegisterPassengerTests : PassengerIntegrationTestBase
{
    private const string CompleteRegistrationRoute = "api/v1.0/passenger/complete-registration";

    public CompleteRegisterPassengerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_complete_register_passenger_and_update_to_db()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var passenger = CreatePassenger(userId, "123456789");

        await Fixture.InsertAsync(passenger);

        var command = new FakeCompleteRegisterPassengerCommand(
            passenger.PassportNumber,
            passenger.Id,
            userId
        ).Generate();

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        response.Should().NotBeNull();
        response?.PassengerDto?.Name.Should().Be(passenger.Name);
        response?.PassengerDto?.PassportNumber.Should().Be(command.PassportNumber);
        response?.PassengerDto?.PassengerType.ToString().Should().Be(command.PassengerType.ToString());
        response?.PassengerDto?.Age.Should().Be(command.Age);
    }

    [Fact]
    public async Task should_not_complete_register_passenger_owned_by_another_user()
    {
        // Arrange
        var victim = CreatePassenger(Guid.CreateVersion7(), "123456789");

        await Fixture.InsertAsync(victim);

        var attackerUserId = Guid.CreateVersion7();
        var command = new FakeCompleteRegisterPassengerCommand(
            victim.PassportNumber,
            victim.Id,
            attackerUserId
        ).Generate();

        // Act
        var act = () => Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<PassengerNotExist>();

        var stored = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(victim.Id);
        stored!.PassengerType.Should().Be(PassengerType.Unknown);
        stored.Age.Should().BeNull();
    }

    [Fact]
    public async Task should_reject_complete_registration_request_for_another_users_passport_number()
    {
        // Arrange
        var victim = CreatePassenger(Guid.CreateVersion7(), "123456789");

        await Fixture.InsertAsync(victim);

        var request = new CompleteRegisterPassengerRequestDto(victim.PassportNumber, PassengerType.Male, 30);

        // Act
        var result = await CreateClientForUser(Guid.CreateVersion7())
            .PostAsJsonAsync(CompleteRegistrationRoute, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var stored = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(victim.Id);
        stored!.PassengerType.Should().Be(PassengerType.Unknown);
        stored.Age.Should().BeNull();
    }

    [Fact]
    public async Task should_complete_registration_request_for_own_passport_number()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var passenger = CreatePassenger(userId, "123456789");

        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 30);

        // Act
        var result = await CreateClientForUser(userId).PostAsJsonAsync(CompleteRegistrationRoute, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<CompleteRegisterPassengerResponseDto>();
        response?.PassengerDto?.PassportNumber.Should().Be(passenger.PassportNumber);
        response?.PassengerDto?.PassengerType.Should().Be(PassengerType.Male);
        response?.PassengerDto?.Age.Should().Be(30);
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

    private HttpClient CreateClientForUser(Guid userId)
    {
        var httpClient = Fixture.HttpClient;

        httpClient.SetFakeBearerToken(
            new Dictionary<string, object>
            {
                { ClaimTypes.Name, "test@sample.com" },
                { ClaimTypes.NameIdentifier, userId.ToString() },
                { ClaimTypes.Role, "admin" },
                { "scope", "flight-api" },
            }
        );

        return httpClient;
    }
}
