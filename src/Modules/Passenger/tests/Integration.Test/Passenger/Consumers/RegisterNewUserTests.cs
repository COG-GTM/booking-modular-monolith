using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using BuildingBlocks.Web;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Passenger.Data;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Integration.Test.Passenger.Consumers;

public class RegisterNewUserTests : PassengerIntegrationTestBase
{
    public RegisterNewUserTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_create_passenger_linked_to_identity_user_id()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        // Act
        await ConsumeAsync(userCreated);

        // Assert
        var passenger = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.AsNoTracking().SingleAsync(x => x.PassportNumber.Value == userCreated.PassportNumber)
        );

        passenger.UserId.Should().Be(userCreated.Id);
        passenger.Name.Value.Should().Be(userCreated.Name);
    }

    [Fact]
    public async Task should_not_rebind_existing_passenger_to_another_user()
    {
        // Arrange
        var ownerId = Guid.CreateVersion7();
        var existing = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789"),
            ownerId
        );
        await Fixture.InsertAsync(existing);

        var userCreated = new FakeUserCreated().Generate();

        // Act
        await ConsumeAsync(userCreated);

        // Assert
        var passengers = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.AsNoTracking().Where(x => x.PassportNumber.Value == userCreated.PassportNumber).ToListAsync()
        );

        passengers.Should().ContainSingle().Which.UserId.Should().Be(ownerId);
    }

    private async Task ConsumeAsync(UserCreated message)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var sp = scope.ServiceProvider;

        var handler = new RegisterNewUserHandler(
            sp.GetRequiredService<PassengerDbContext>(),
            sp.GetRequiredService<IEventDispatcher>(),
            sp.GetRequiredService<ILogger<RegisterNewUserHandler>>(),
            sp.GetRequiredService<IOptions<AppOptions>>()
        );

        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(message);

        await handler.Consume(context);
    }
}
