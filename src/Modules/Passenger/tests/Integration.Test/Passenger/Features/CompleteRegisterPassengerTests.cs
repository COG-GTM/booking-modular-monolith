using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Exception;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.AspNetCore.Mvc;
using Passenger.Data;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public class CompleteRegisterPassengerTests : PassengerIntegrationTestBase
{
    private const string Route = "api/v1.0/passenger/complete-registration";

    public CompleteRegisterPassengerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_complete_register_passenger_and_update_to_db()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        await Fixture.InsertAsync(passenger);

        var command = new FakeCompleteRegisterPassengerCommand(passenger.PassportNumber, passenger.Id).Generate();

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
    public async Task should_throw_passenger_not_exist_when_passport_number_is_not_registered()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand("000000000", Guid.CreateVersion7()).Generate();

        // Act
        var act = () => Fixture.SendAsync(command);

        // Assert
        var exception = await act.Should().ThrowAsync<PassengerNotExist>();
        exception.Which.Message.Should().Be("Please register before!");
    }

    [Fact]
    public async Task should_not_change_registered_passenger_when_another_passport_number_is_not_registered()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("223456789")
        );

        await Fixture.InsertAsync(passenger);

        var command = new FakeCompleteRegisterPassengerCommand("999999999", Guid.CreateVersion7()).Generate();

        // Act
        var act = () => Fixture.SendAsync(command);

        // Assert
        await act.Should().ThrowAsync<PassengerNotExist>();

        var untouched = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(
            passenger.Id
        );
        untouched.Should().NotBeNull();
        untouched!.Age.Should().BeNull();
        untouched.PassengerType.Should().Be(default(PassengerType));
    }

    [Fact]
    public async Task should_throw_validation_exception_when_age_is_not_greater_than_zero()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("323456789")
        );

        await Fixture.InsertAsync(passenger);

        var command = new CompleteRegisterPassenger(passenger.PassportNumber, PassengerType.Male, 0);

        // Act
        var act = () => Fixture.SendAsync(command);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Message.Should().Be("The Age must be greater than 0!");
    }

    [Fact]
    public async Task should_throw_validation_exception_when_passport_number_is_null()
    {
        // Arrange
        var command = new CompleteRegisterPassenger(null!, PassengerType.Male, 30);

        // Act
        var act = () => Fixture.SendAsync(command);

        // Assert
        var exception = await act.Should().ThrowAsync<ValidationException>();
        exception.Which.Message.Should().Be("The PassportNumber is required!");
    }

    [Fact]
    public async Task should_complete_register_passenger_through_http_endpoint()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("423456789")
        );

        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Female, 25);

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(Route, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<CompleteRegisterPassengerResponseDto>();
        response.Should().NotBeNull();
        response!.PassengerDto.Id.Should().Be(passenger.Id.Value);
        response.PassengerDto.Name.Should().Be(passenger.Name);
        response.PassengerDto.PassportNumber.Should().Be(request.PassportNumber);
        response.PassengerDto.PassengerType.Should().Be(request.PassengerType);
        response.PassengerDto.Age.Should().Be(request.Age);

        var updated = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(passenger.Id);
        updated.Should().NotBeNull();
        updated!.Age!.Value.Should().Be(request.Age);
        updated.PassengerType.Should().Be(request.PassengerType);
    }

    [Fact]
    public async Task should_return_bad_request_from_http_endpoint_when_passport_number_is_not_registered()
    {
        // Arrange
        var request = new CompleteRegisterPassengerRequestDto("000000001", PassengerType.Male, 30);

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(Route, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await result.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be(nameof(PassengerNotExist));
        problem.Detail.Should().Be("Please register before!");
        problem.Status.Should().Be((int)HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_return_bad_request_from_http_endpoint_when_age_is_not_greater_than_zero()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("523456789")
        );

        await Fixture.InsertAsync(passenger);

        var request = new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 0);

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(Route, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await result.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Should().NotBeNull();
        problem!.Title.Should().Be(nameof(ValidationException));
        problem.Detail.Should().Be("The Age must be greater than 0!");
    }

    [Fact]
    public async Task should_return_unauthorized_from_http_endpoint_without_bearer_token()
    {
        // Arrange
        var request = new CompleteRegisterPassengerRequestDto("623456789", PassengerType.Male, 30);

        var client = Fixture.HttpClient;
        client.DefaultRequestHeaders.Authorization = null;

        // Act
        var result = await client.PostAsJsonAsync(Route, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
