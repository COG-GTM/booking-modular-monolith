using System;
using System.Threading;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Passenger.Data;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Passenger.Passengers.Exceptions;
using Passenger.Passengers.ValueObjects;
using Xunit;
using PassengerModel = global::Passenger.Passengers.Models.Passenger;

namespace Integration.Test.Passenger.Consumers;

public class RegisterNewUserSoftDeletedPassengerTests : PassengerIntegrationTestBase
{
    public RegisterNewUserSoftDeletedPassengerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Theory]
    [InlineData(true, "AB1234567")]
    [InlineData(false, "AB1234567")]
    [InlineData(true, " ab1234567 ")]
    [InlineData(false, " ab1234567 ")]
    public async Task should_preserve_soft_deleted_passenger_and_enforce_passport_ownership(
        bool sameUser,
        string incomingPassportNumber
    )
    {
        var message = new FakeUserCreated().Generate() with { PassportNumber = "AB1234567" };
        var passengerId = PassengerId.Of(Guid.CreateVersion7());
        var passenger = PassengerModel.Create(
            passengerId,
            Name.Of(message.Name),
            PassportNumber.Of(message.PassportNumber),
            message.Id,
            isDeleted: true
        );
        using (var scope = Fixture.ServiceProvider.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PassengerDbContext>();
            dbContext.Passengers.Add(passenger);
            await dbContext.SaveChangesAsync();
        }

        var incomingMessage = message with
        {
            Id = sameUser ? message.Id : Guid.CreateVersion7(),
            Name = "Other User",
            PassportNumber = incomingPassportNumber,
        };
        var act = () => ConsumeAsync(incomingMessage);

        if (sameUser)
        {
            await act.Should().NotThrowAsync();
        }
        else
        {
            await act.Should().ThrowAsync<PassportNumberAlreadyLinkedException>();
        }

        using var verificationScope = Fixture.ServiceProvider.CreateScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<PassengerDbContext>();
        var passengers = await verificationContext.Passengers.IgnoreQueryFilters().ToListAsync();
        passengers.Should().ContainSingle();
        passengers[0].Id.Should().Be(passengerId);
        passengers[0].UserId.Should().Be(message.Id);
        passengers[0].Name.Value.Should().Be(message.Name);
        passengers[0].PassportNumber.Value.Should().Be(message.PassportNumber);
        passengers[0].IsDeleted.Should().BeTrue();
    }

    private async Task ConsumeAsync(UserCreated message)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var handler = ActivatorUtilities.CreateInstance<RegisterNewUserHandler>(scope.ServiceProvider);
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);

        await handler.Consume(context);
    }
}
