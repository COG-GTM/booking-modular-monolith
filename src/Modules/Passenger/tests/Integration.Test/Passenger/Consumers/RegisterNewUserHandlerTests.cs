using System;
using System.Linq;
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

public class RegisterNewUserHandlerTests : PassengerIntegrationTestBase
{
    public RegisterNewUserHandlerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_create_passenger_with_user_id_and_passport_number()
    {
        var message = new FakeUserCreated().Generate();

        await ConsumeAsync(message);

        using var scope = Fixture.ServiceProvider.CreateScope();
        var passenger = await scope
            .ServiceProvider.GetRequiredService<PassengerDbContext>()
            .Passengers.IgnoreQueryFilters()
            .SingleAsync();

        passenger.UserId.Should().Be(message.Id);
        passenger.PassportNumber.Value.Should().Be(message.PassportNumber);
    }

    [Fact]
    public async Task should_ignore_duplicate_user_created_message()
    {
        var message = new FakeUserCreated().Generate();

        await ConsumeAsync(message);
        await ConsumeAsync(message);

        using var scope = Fixture.ServiceProvider.CreateScope();
        var passengers = await scope
            .ServiceProvider.GetRequiredService<PassengerDbContext>()
            .Passengers.IgnoreQueryFilters()
            .ToListAsync();

        passengers.Should().ContainSingle();
        passengers[0].UserId.Should().Be(message.Id);
    }

    [Fact]
    public async Task should_reject_passport_number_linked_to_another_user()
    {
        var firstMessage = new FakeUserCreated().Generate();
        await ConsumeAsync(firstMessage);

        var secondMessage = firstMessage with { Id = Guid.CreateVersion7(), Name = "Different User" };

        var act = () => ConsumeAsync(secondMessage);

        await act.Should().ThrowAsync<PassportNumberAlreadyLinkedException>();

        using var scope = Fixture.ServiceProvider.CreateScope();
        var passenger = await scope
            .ServiceProvider.GetRequiredService<PassengerDbContext>()
            .Passengers.IgnoreQueryFilters()
            .SingleAsync();
        passenger.UserId.Should().Be(firstMessage.Id);
        passenger.Name.Value.Should().Be(firstMessage.Name);
    }

    [Fact]
    public async Task should_enforce_unique_passport_number_in_database()
    {
        var first = CreatePassenger(Guid.CreateVersion7(), Guid.CreateVersion7());
        var second = CreatePassenger(Guid.CreateVersion7(), Guid.CreateVersion7());

        using var scope = Fixture.ServiceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PassengerDbContext>();
        dbContext.Passengers.AddRange(first, second);

        var act = () => dbContext.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
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

    private static PassengerModel CreatePassenger(Guid id, Guid userId)
    {
        return PassengerModel.Create(PassengerId.Of(id), Name.Of("First User"), PassportNumber.Of("123456789"), userId);
    }
}
