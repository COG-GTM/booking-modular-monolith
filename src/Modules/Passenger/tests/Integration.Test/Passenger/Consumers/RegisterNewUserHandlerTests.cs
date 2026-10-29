using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Web;
using FluentAssertions;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Passenger.Data;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Xunit;

namespace Integration.Test.Passenger.Consumers;

public class RegisterNewUserHandlerTests : IDisposable
{
    private readonly PassengerDbContext _dbContext = new(
        new DbContextOptionsBuilder<PassengerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
    );

    private readonly IEventDispatcher _eventDispatcher = Substitute.For<IEventDispatcher>();

    [Fact]
    public async Task passenger_is_created_with_the_identity_user_id()
    {
        var message = new FakeUserCreated().Generate();

        await CreateHandler().Consume(ContextFor(message));

        var passenger = await _dbContext.Passengers.SingleAsync();
        passenger.Id.Value.Should().Be(message.Id);
        passenger.Name.Value.Should().Be(message.Name);
        passenger.PassportNumber.Value.Should().Be(message.PassportNumber);
    }

    [Fact]
    public async Task passenger_ids_follow_each_user_id_instead_of_being_generated()
    {
        var first = new UserCreated(Guid.NewGuid(), "Alice", "111111111");
        var second = new UserCreated(Guid.NewGuid(), "Bob", "222222222");

        await CreateHandler().Consume(ContextFor(first));
        await CreateHandler().Consume(ContextFor(second));

        var ids = await _dbContext.Passengers.Select(p => p.Id).ToListAsync();
        ids.Select(id => id.Value).Should().BeEquivalentTo(new[] { first.Id, second.Id });
    }

    [Fact]
    public async Task passenger_created_event_carries_the_identity_user_id()
    {
        var message = new FakeUserCreated().Generate();

        await CreateHandler().Consume(ContextFor(message));

        _ = _eventDispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<PassengerCreatedDomainEvent>(e =>
                    e.Id == message.Id && e.PassportNumber == message.PassportNumber
                ),
                typeof(IInternalCommand),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task existing_passport_number_does_not_create_a_second_passenger()
    {
        var original = new UserCreated(Guid.NewGuid(), "Sam", "123456789");
        await CreateHandler().Consume(ContextFor(original));
        _eventDispatcher.ClearReceivedCalls();

        await CreateHandler().Consume(ContextFor(original with { Id = Guid.NewGuid() }));

        var passenger = await _dbContext.Passengers.SingleAsync();
        passenger.Id.Value.Should().Be(original.Id);
        _eventDispatcher.ReceivedCalls().Should().BeEmpty();
    }

    public void Dispose() => _dbContext.Dispose();

    private RegisterNewUserHandler CreateHandler() =>
        new(
            _dbContext,
            _eventDispatcher,
            NullLogger<RegisterNewUserHandler>.Instance,
            Options.Create(new AppOptions { Name = "passenger-test" })
        );

    private static ConsumeContext<UserCreated> ContextFor(UserCreated message)
    {
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(message);
        return context;
    }
}
