using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Xunit;

namespace Host.Test.Features;

public class RegisterNewUserHostTests : PassengerHostTestBase
{
    public RegisterNewUserHostTests(
        TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_consume_user_created_from_broker_in_passenger_host()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        (await Fixture.WaitForConsuming<UserCreated>())
            .Should()
            .Be(true);

        var passenger = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.SingleOrDefaultAsync(x => x.PassportNumber.Value == userCreated.PassportNumber)
        );

        passenger.Should().NotBeNull();
        passenger?.Name.Value.Should().Be(userCreated.Name);
    }
}
