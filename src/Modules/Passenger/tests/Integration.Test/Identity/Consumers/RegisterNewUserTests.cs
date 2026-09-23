using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Identity.Consumers;

public class RegisterNewUserTests : PassengerIntegrationTestBase
{
    public RegisterNewUserTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_create_passenger_and_publish_passenger_created_when_user_created_is_consumed()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        (await Fixture.WaitForConsuming<UserCreated>(x => x.Context.Message.Id == userCreated.Id))
            .Should()
            .Be(true);

        var passengers = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.Where(x => x.PassportNumber.Value == userCreated.PassportNumber).ToListAsync()
        );

        passengers.Should().ContainSingle();

        var passenger = passengers.Single();
        passenger.Name.Value.Should().Be(userCreated.Name);
        passenger.PassportNumber.Value.Should().Be(userCreated.PassportNumber);
        passenger.PassengerType.Should().Be(global::Passenger.Passengers.Enums.PassengerType.Unknown);
        passenger.IsDeleted.Should().BeFalse();

        (await Fixture.WaitForPublishing<PassengerCreated>(x => x.Context.Message.Id == passenger.Id.Value))
            .Should()
            .Be(true);
    }

    [Fact]
    public async Task should_not_create_duplicate_passenger_when_same_user_created_is_consumed_twice()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        await Fixture.Publish(userCreated);
        (await Fixture.WaitForConsuming<UserCreated>(x => x.Context.Message.Id == userCreated.Id)).Should().Be(true);

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        (await Fixture.WaitForConsuming<UserCreated>(x => x.Context.Message.Id == userCreated.Id, count: 2))
            .Should()
            .Be(true);

        var passengers = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.Where(x => x.PassportNumber.Value == userCreated.PassportNumber).ToListAsync()
        );

        passengers.Should().ContainSingle();
    }

    [Fact]
    public async Task should_skip_creation_when_passenger_with_same_passport_number_already_exists()
    {
        // Arrange
        var existing = global::Passenger.Passengers.Models.Passenger.Create(
            global::Passenger.Passengers.ValueObjects.PassengerId.Of(Guid.CreateVersion7()),
            global::Passenger.Passengers.ValueObjects.Name.Of("Existing"),
            global::Passenger.Passengers.ValueObjects.PassportNumber.Of("987654321")
        );

        await Fixture.InsertAsync(existing);

        var userCreated = new FakeUserCreated()
            .RuleFor(r => r.PassportNumber, _ => existing.PassportNumber.Value)
            .Generate();

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        (await Fixture.WaitForConsuming<UserCreated>(x => x.Context.Message.Id == userCreated.Id))
            .Should()
            .Be(true);

        var passengers = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.Where(x => x.PassportNumber.Value == existing.PassportNumber.Value).ToListAsync()
        );

        passengers.Should().ContainSingle();
        passengers.Single().Id.Should().Be(existing.Id);
        passengers.Single().Name.Value.Should().Be("Existing");
    }
}
