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
    public async Task should_create_passenger_when_user_created_integration_event_is_consumed()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        var passengerCreated = false;
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (!passengerCreated && DateTime.UtcNow < deadline)
        {
            passengerCreated = await Fixture.ExecuteDbContextAsync(db =>
                db.Passengers.AnyAsync(x => x.PassportNumber.Value == userCreated.PassportNumber)
            );

            if (!passengerCreated)
            {
                await Task.Delay(200);
            }
        }

        passengerCreated.Should().BeTrue();
    }
}
