using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Identity.Features;

public class RegisterNewUserTests : PassengerIntegrationTestBase
{
    public RegisterNewUserTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_store_owning_user_id_on_passenger_created_from_user_created_event()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        // Act
        await Fixture.Publish(userCreated);

        // Assert
        global::Passenger.Passengers.Models.Passenger? passenger = null;
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (passenger is null && DateTime.UtcNow < deadline)
        {
            passenger = await Fixture.ExecuteDbContextAsync(db =>
                db.Passengers.AsNoTracking().SingleOrDefaultAsync(x => x.UserId == userCreated.Id));

            if (passenger is null)
            {
                await Task.Delay(200);
            }
        }

        passenger.Should().NotBeNull();
        passenger!.PassportNumber.Value.Should().Be(userCreated.PassportNumber);
    }
}
