using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Consumers;

using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;

public class RegisterNewUserConsumerTests : PassengerIntegrationTestBase
{
    public RegisterNewUserConsumerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_consume_user_created_from_broker_and_create_passenger()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();
        var harness = Fixture.ServiceProvider.GetTestHarness();

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        (await harness.Published.Any<UserCreated>(x => x.Context.Message.Id == userCreated.Id))
            .Should()
            .BeTrue();

        var consumerHarness = harness.GetConsumerHarness<RegisterNewUserHandler>();
        (await consumerHarness.Consumed.Any<UserCreated>(x => x.Context.Message.Id == userCreated.Id))
            .Should()
            .BeTrue();

        var passenger = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.SingleOrDefaultAsync(p => p.PassportNumber.Value == userCreated.PassportNumber)
        );

        passenger.Should().NotBeNull();
        passenger!.Name.Value.Should().Be(userCreated.Name);
    }
}
