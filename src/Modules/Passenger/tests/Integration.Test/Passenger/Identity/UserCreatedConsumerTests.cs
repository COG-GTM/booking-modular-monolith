using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using Microsoft.EntityFrameworkCore;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Identity;

public class UserCreatedConsumerTests : PassengerIntegrationTestBase
{
    public UserCreatedConsumerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_create_passenger_when_user_created_is_consumed()
    {
        var message = new UserCreated(Guid.NewGuid(), "Sam", "123456789");
        await Fixture.Publish(message);

        global::Passenger.Passengers.Models.Passenger? passenger = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (passenger is null && DateTime.UtcNow < deadline)
        {
            passenger = await Fixture.ExecuteDbContextAsync(db =>
                db.Passengers.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.PassportNumber.Value == message.PassportNumber)
            );
            if (passenger is null)
            {
                await Task.Delay(100);
            }
        }

        Assert.NotNull(passenger);
        Assert.Equal(message.Name, passenger.Name.Value);
        Assert.Equal(message.PassportNumber, passenger.PassportNumber.Value);
    }
}
