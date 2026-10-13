using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using BuildingBlocks.Constants;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class CompleteRegisterPassengerAuthorizationTests : PassengerIntegrationTestBase
{
    private const string Route = "api/v1/passenger/complete-registration";

    public CompleteRegisterPassengerAuthorizationTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_throw_access_denied_when_passenger_has_no_linked_user()
    {
        // Arrange: legacy rows have user_id = NULL and must fail closed for non-admins
        var userId = Guid.CreateVersion7();
        var passenger = CreatePassenger("222222222", userId: null);

        await Fixture.InsertAsync(passenger);
        Fixture.SetCurrentUser(userId, IdentityConstant.Role.User);

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

        // Act
        var act = async () => await Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<PassengerAccessDeniedException>();
    }

    [Fact]
    public async Task should_allow_admin_to_complete_register_passenger_without_linked_user()
    {
        // Arrange
        var passenger = CreatePassenger("333333333", userId: null);

        await Fixture.InsertAsync(passenger);
        Fixture.SetCurrentUser(Guid.CreateVersion7(), IdentityConstant.Role.Admin);

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

        // Act
        var response = await Fixture.SendAsync(command);

        // Assert
        response?.PassengerDto?.PassengerType.Should().Be(command.PassengerType);
        response?.PassengerDto?.Age.Should().Be(command.Age);
    }

    [Fact]
    public async Task should_not_persist_changes_when_access_is_denied()
    {
        // Arrange
        var passenger = CreatePassenger("444444444", Guid.CreateVersion7());

        await Fixture.InsertAsync(passenger);
        Fixture.SetCurrentUser(Guid.CreateVersion7(), IdentityConstant.Role.User);

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

        // Act
        var act = async () => await Fixture.SendAsync(command);
        await act.Should().ThrowAsync<PassengerAccessDeniedException>();

        // Assert
        var stored = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.AsNoTracking().SingleAsync(x => x.Id == passenger.Id)
        );

        stored.PassengerType.Should().Be(PassengerType.Unknown);
        stored.Age.Should().BeNull();
        stored.UserId.Should().Be(passenger.UserId);
    }

    [Fact]
    public async Task should_return_403_problem_details_over_http_when_caller_is_not_owner()
    {
        // Arrange
        var passenger = CreatePassenger("777777777", Guid.CreateVersion7());
        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 30);

        // Act
        var response = await CreateClient(Guid.CreateVersion7(), IdentityConstant.Role.User)
            .PostAsJsonAsync(Route, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        problem.Should().NotBeNull();
        problem!.Status.Should().Be((int)HttpStatusCode.Forbidden);
        problem.Title.Should().Be(nameof(PassengerAccessDeniedException));
        problem.Detail.Should().Be("You are not allowed to modify this passenger!");
    }

    [Fact]
    public async Task should_complete_registration_over_http_when_sub_claim_matches_passenger_owner()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var passenger = CreatePassenger("888888888", userId);
        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Female, 25);

        // Act
        var response = await CreateClient(userId, IdentityConstant.Role.User).PostAsJsonAsync(Route, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<CompleteRegisterPassengerResponseDto>();

        body?.PassengerDto?.PassportNumber.Should().Be(passenger.PassportNumber);
        body?.PassengerDto?.PassengerType.Should().Be(PassengerType.Female);
        body?.PassengerDto?.Age.Should().Be(25);
    }

    [Fact]
    public async Task should_complete_registration_over_http_when_caller_is_admin_but_not_owner()
    {
        // Arrange
        var passenger = CreatePassenger("999999999", Guid.CreateVersion7());
        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 40);

        // Act
        var response = await CreateClient(Guid.CreateVersion7(), IdentityConstant.Role.Admin)
            .PostAsJsonAsync(Route, request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient CreateClient(Guid userId, string role)
    {
        var claims = new Dictionary<string, object>
        {
            { JwtRegisteredClaimNames.Sub, userId.ToString() },
            { ClaimTypes.Name, "test@sample.com" },
            { ClaimTypes.Role, role },
            { "scope", "flight-api" },
        };

        var httpClient = Fixture.HttpClient;
        httpClient.SetFakeBearerToken(claims);

        return httpClient;
    }

    private static global::Passenger.Passengers.Models.Passenger CreatePassenger(string passportNumber, Guid? userId)
    {
        return global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of(passportNumber),
            userId
        );
    }
}
