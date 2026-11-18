using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using MongoDB.Driver;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Data;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public class PassengerOwnershipProjectionTests : PassengerIntegrationTestBase
{
    public PassengerOwnershipProjectionTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_store_registration_owner_in_new_or_existing_read_model(bool existingPassenger)
    {
        var userId = Guid.NewGuid();
        var command = new CompleteRegisterPassengerMongoCommand(
            Guid.NewGuid(),
            "123456789",
            "Sam",
            PassengerType.Male,
            30,
            UserId: userId
        );
        if (existingPassenger)
        {
            await Fixture.SendAsync(command with { UserId = null });
        }

        await Fixture.SendAsync(command);

        var passenger = await Fixture.ExecuteReadContextAsync(db =>
            db.Passenger.Find(x => x.PassengerId == command.Id).SingleAsync()
        );
        passenger.UserId.Should().Be(userId);
    }
}
