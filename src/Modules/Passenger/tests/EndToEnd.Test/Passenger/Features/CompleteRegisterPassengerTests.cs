using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.TestBase;
using EndToEnd.Test.Fakes;
using EndToEnd.Test.Routes;
using FluentAssertions;
using Passenger.Data;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace EndToEnd.Test.Passenger.Features;

public class CompleteRegisterPassengerTests : PassengerEndToEndTestBase
{
    public CompleteRegisterPassengerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task should_complete_register_passenger_through_http()
    {
        // Arrange
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("987654321")
        );

        await Fixture.InsertAsync(passenger);

        var request = new FakeCompleteRegisterPassengerRequestDto(passenger.PassportNumber).Generate();

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Passenger.CompleteRegistration, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await result.Content.ReadFromJsonAsync<CompleteRegisterPassengerResponseDto>();
        response.Should().NotBeNull();
        response!.PassengerDto.PassportNumber.Should().Be(request.PassportNumber);
        response.PassengerDto.Age.Should().Be(request.Age);
        response.PassengerDto.PassengerType.Should().Be(request.PassengerType);
    }

    [Fact]
    public async Task should_return_bad_request_when_passport_number_is_empty()
    {
        // Arrange
        var request = new FakeCompleteRegisterPassengerRequestDto(string.Empty).Generate();

        // Act
        var result = await Fixture.HttpClient.PostAsJsonAsync(ApiRoutes.Passenger.CompleteRegistration, request);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
