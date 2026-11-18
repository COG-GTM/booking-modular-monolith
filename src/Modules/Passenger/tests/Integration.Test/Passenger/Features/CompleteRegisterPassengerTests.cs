using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.ValueObjects;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class CompleteRegisterPassengerTests : PassengerIntegrationTestBase
{
    public CompleteRegisterPassengerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_complete_register_passenger_and_update_to_db()
    {
        // Arrange
        var userId = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            userId
        );

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
    public async Task should_not_complete_register_passenger_for_another_user()
    {
        var userA = Guid.CreateVersion7();
        var userB = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            userA
        );
        await Fixture.InsertAsync(passenger);
        var command = new FakeCompleteRegisterPassengerCommand(
            passenger.PassportNumber,
            passenger.Id,
            userB
        ).Generate();

        Func<Task> act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<PassengerNotExist>();
        var persistedPassenger = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.SingleAsync(x => x.Id == passenger.Id)
        );
        persistedPassenger.Age.Should().BeNull();
        persistedPassenger.PassengerType.Should().Be(PassengerType.Unknown);
    }

    [Fact]
    public async Task should_not_complete_register_passenger_for_legacy_passenger_without_user_id()
    {
        var userId = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );
        await Fixture.InsertAsync(passenger);
        var command = new FakeCompleteRegisterPassengerCommand(
            passenger.PassportNumber,
            passenger.Id,
            userId
        ).Generate();

        Func<Task> act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<PassengerNotExist>();
        var persistedPassenger = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.SingleAsync(x => x.Id == passenger.Id)
        );
        persistedPassenger.Age.Should().BeNull();
        persistedPassenger.PassengerType.Should().Be(PassengerType.Unknown);
    }

    [Fact]
    public async Task should_complete_register_passenger_via_api_for_owner()
    {
        var userId = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("11111111"),
            userId
        );
        await Fixture.InsertAsync(passenger);

        var client = Fixture.HttpClient;
        client.SetFakeBearerToken(
            new Dictionary<string, object> { { "sub", userId.ToString() }, { "scope", "flight-api" } }
        );

        var response = await client.PostAsJsonAsync(
            "api/v1/passenger/complete-registration",
            new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 35)
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var persistedPassenger = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.SingleAsync(x => x.Id == passenger.Id)
        );
        persistedPassenger.Age?.Value.Should().Be(35);
        persistedPassenger.PassengerType.Should().Be(PassengerType.Male);
    }

    [Fact]
    public async Task should_reject_complete_register_passenger_via_api_without_user_claim()
    {
        var userId = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("22222222"),
            userId
        );
        await Fixture.InsertAsync(passenger);

        var client = Fixture.HttpClient;
        client.SetFakeBearerToken(new Dictionary<string, object> { { "scope", "flight-api" } });

        var response = await client.PostAsJsonAsync(
            "api/v1/passenger/complete-registration",
            new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 35)
        );

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var persistedPassenger = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.SingleAsync(x => x.Id == passenger.Id)
        );
        persistedPassenger.Age.Should().BeNull();
        persistedPassenger.PassengerType.Should().Be(PassengerType.Unknown);
    }

    [Fact]
    public async Task should_not_complete_register_passenger_via_api_for_another_user()
    {
        var ownerId = Guid.CreateVersion7();
        var otherUserId = Guid.CreateVersion7();
        var passenger = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("33333333"),
            ownerId
        );
        await Fixture.InsertAsync(passenger);

        var client = Fixture.HttpClient;
        client.SetFakeBearerToken(
            new Dictionary<string, object> { { "sub", otherUserId.ToString() }, { "scope", "flight-api" } }
        );

        var response = await client.PostAsJsonAsync(
            "api/v1/passenger/complete-registration",
            new CompleteRegisterPassengerRequestDto(passenger.PassportNumber, PassengerType.Male, 35)
        );

        response.IsSuccessStatusCode.Should().BeFalse();
        var persistedPassenger = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.SingleAsync(x => x.Id == passenger.Id)
        );
        persistedPassenger.Age.Should().BeNull();
        persistedPassenger.PassengerType.Should().Be(PassengerType.Unknown);
    }
}
