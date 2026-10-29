using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class PassengerInboxTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> fixture)
    : PassengerIntegrationTestBase(fixture)
{
    [Fact]
    public async Task duplicate_message_id_is_skipped_and_inbox_belongs_to_passenger()
    {
        var id = Guid.NewGuid();
        var bus = Fixture.ServiceProvider.GetRequiredService<IBus>();
        var message = new UserCreated(Guid.NewGuid(), "Inbox User", "987654321");
        await bus.Publish(message, context => context.MessageId = id);
        await WaitForProcessed(id);

        await bus.Publish(new UserCreated(Guid.NewGuid(), "Duplicate", "987654322"), context => context.MessageId = id);
        var sentinelId = Guid.NewGuid();
        await bus.Publish(
            new UserCreated(Guid.NewGuid(), "Sentinel", "987654323"),
            context => context.MessageId = sentinelId
        );
        await WaitForProcessed(sentinelId);

        using var scope = Fixture.ServiceProvider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<
            PersistMessageDbContext<global::Passenger.PassengerRoot>
        >();
        var inboxId = InboxMessageId.For("passenger.register-new-user-handler", id);
        (await store.PersistMessage.CountAsync(x => x.Id == inboxId)).Should().Be(1);
        var passengers = scope.ServiceProvider.GetRequiredService<PassengerDbContext>();
        (await passengers.Passengers.CountAsync(x => x.PassportNumber.Value == "987654321")).Should().Be(1);
        (await passengers.Passengers.AnyAsync(x => x.PassportNumber.Value == "987654322")).Should().BeFalse();
    }

    private async Task WaitForProcessed(Guid id)
    {
        id = InboxMessageId.For("passenger.register-new-user-handler", id);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var scope = Fixture.ServiceProvider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<
                PersistMessageDbContext<global::Passenger.PassengerRoot>
            >();
            if (
                await store.PersistMessage.AnyAsync(
                    x => x.Id == id && x.MessageStatus == MessageStatus.Processed,
                    timeout.Token
                )
            )
                return;
            await Task.Delay(50, timeout.Token);
        }
    }
}
