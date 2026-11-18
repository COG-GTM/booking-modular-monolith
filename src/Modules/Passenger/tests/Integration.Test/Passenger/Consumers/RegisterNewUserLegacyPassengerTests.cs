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

public class RegisterNewUserLegacyPassengerTests : PassengerIntegrationTestBase
{
    public RegisterNewUserLegacyPassengerTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_reject_user_created_when_passport_belongs_to_legacy_passenger_without_owner()
    {
        await Fixture.InsertAsync(CreatePassenger(Guid.Empty, "LG1234567"));

        var message = new FakeUserCreated().Generate() with { PassportNumber = "LG1234567" };

        var act = () => ConsumeAsync(message);

        await act.Should().ThrowAsync<PassportNumberAlreadyLinkedException>();
        (await SinglePassengerAsync()).UserId.Should().Be(Guid.Empty);
    }

    private async Task<PassengerModel> SinglePassengerAsync()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<PassengerDbContext>()
            .Passengers.IgnoreQueryFilters()
            .SingleAsync();
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

    private static PassengerModel CreatePassenger(Guid userId, string passportNumber)
    {
        return PassengerModel.Create(
            PassengerId.Of(Guid.CreateVersion7()),
            Name.Of("Existing Owner"),
            PassportNumber.Of(passportNumber),
            userId
        );
    }
}
