using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Passenger.Passengers.Enums;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Integration.Test.Identity.Consumers;

using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public class RegisterNewUserTests : PassengerIntegrationTestBase
{
    private const string CompleteRegistrationRoute = "api/v1.0/passenger/complete-registration";

    public RegisterNewUserTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_create_passenger_owned_by_the_created_user()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        var passenger = await WaitForPassengerAsync(userCreated.PassportNumber);

        passenger.Should().NotBeNull();
        passenger!.UserId.Should().Be(userCreated.Id);
        passenger.Name.Value.Should().Be(userCreated.Name);
        passenger.PassportNumber.Value.Should().Be(userCreated.PassportNumber);
    }

    [Fact]
    public async Task created_user_should_be_able_to_complete_own_registration()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        await Fixture.Publish(userCreated);

        var passenger = await WaitForPassengerAsync(userCreated.PassportNumber);
        passenger.Should().NotBeNull();

        var request = new CompleteRegisterPassengerRequestDto(userCreated.PassportNumber, PassengerType.Male, 30);

        // Act
        var result = await CreateClientForUser(userCreated.Id).PostAsJsonAsync(CompleteRegistrationRoute, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<CompleteRegisterPassengerResponseDto>();
        response?.PassengerDto?.Id.Should().Be(passenger!.Id.Value);
        response?.PassengerDto?.PassengerType.Should().Be(PassengerType.Male);
        response?.PassengerDto?.Age.Should().Be(30);
    }

    private async Task<global::Passenger.Passengers.Models.Passenger?> WaitForPassengerAsync(string passportNumber)
    {
        var harness = Fixture.ServiceProvider.GetTestHarness();

        (await harness.Consumed.Any<UserCreated>(x => x.Context.Message.PassportNumber == passportNumber))
            .Should()
            .BeTrue();

        var timeout = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < timeout)
        {
            var passenger = await Fixture.ExecuteDbContextAsync(db =>
                db.Passengers.SingleOrDefaultAsync(x => x.PassportNumber.Value == passportNumber)
            );

            if (passenger is not null)
            {
                return passenger;
            }

            await Task.Delay(100);
        }

        return null;
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
