using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class RegisterNewUserConsumerTests : PassengerIntegrationTestBase
{
    public RegisterNewUserConsumerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory)
    {
    }

    [Fact]
    public async Task should_consume_user_created_and_create_passenger_in_db()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        var passenger = await WaitForPassengerAsync(userCreated.PassportNumber);

        passenger.Should().NotBeNull();
        passenger!.Name.Value.Should().Be(userCreated.Name);
        passenger.PassportNumber.Value.Should().Be(userCreated.PassportNumber);
    }

    [Fact]
    public async Task should_not_duplicate_passenger_when_passport_number_already_exists()
    {
        // Arrange
        var existing = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Existing"),
            PassportNumber.Of("DUP123456"));

        await Fixture.InsertAsync(existing);

        var userCreated = new FakeUserCreated().Generate() with { PassportNumber = existing.PassportNumber };

        // Act
        await Fixture.Publish(userCreated);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        var count = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.CountAsync(x => x.PassportNumber.Value == existing.PassportNumber.Value));

        count.Should().Be(1);
    }

    private async Task<global::Passenger.Passengers.Models.Passenger?> WaitForPassengerAsync(string passportNumber)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            var passenger = await Fixture.ExecuteDbContextAsync(db =>
                db.Passengers.SingleOrDefaultAsync(x => x.PassportNumber.Value == passportNumber));

            if (passenger is not null)
            {
                return passenger;
            }

            await Task.Delay(200);
        }

        return null;
    }
}
