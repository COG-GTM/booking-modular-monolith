using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Passenger.Data;
using Passenger.Passengers.ValueObjects;
using Xunit;

namespace Integration.Test.Passenger.Features;

using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;

public class RegisterNewUserTests : PassengerIntegrationTestBase
{
    public RegisterNewUserTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_create_passenger_keyed_by_identity_user_id()
    {
        // Arrange
        var userCreated = new FakeUserCreated().Generate();

        // Act
        await ConsumeAsync(userCreated);

        // Assert
        var passenger = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(
            PassengerId.Of(userCreated.Id)
        );

        passenger.Should().NotBeNull();
        passenger!.Id.Value.Should().Be(userCreated.Id);
        passenger.Name.Value.Should().Be(userCreated.Name);
        passenger.PassportNumber.Value.Should().Be(userCreated.PassportNumber);
    }

    [Fact]
    public async Task should_not_create_second_passenger_when_passport_number_already_registered()
    {
        // Arrange
        var existing = global::Passenger.Passengers.Models.Passenger.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Sam"),
            PassportNumber.Of("123456789")
        );

        await Fixture.InsertAsync(existing);

        var userCreated = new FakeUserCreated().Generate();

        // Act
        await ConsumeAsync(userCreated);

        // Assert
        var passenger = await Fixture.FindAsync<global::Passenger.Passengers.Models.Passenger, PassengerId>(
            PassengerId.Of(userCreated.Id)
        );

        passenger.Should().BeNull();
    }

    private async Task ConsumeAsync(UserCreated userCreated)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterNewUserHandler>();

        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(userCreated);

        await handler.Consume(context);
    }
}
