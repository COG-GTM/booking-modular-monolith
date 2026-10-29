using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Passenger.Data;
using Identity.Data;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Integration.Test.Identity.Features;

// Identity -> outbox -> RabbitMQ (Testcontainer) -> passenger.register-new-user-handler queue -> inbox -> Passenger.
public class UserCreatedOverRabbitMqTests : IdentityIntegrationTestBase
{
    private const string PassengerQueue = "passenger.register-new-user-handler";

    public UserCreatedOverRabbitMqTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task registered_user_reaches_passenger_through_the_broker_outbox_and_inbox()
    {
        var passport = $"P{Random.Shared.Next(100_000_000, 999_999_999)}";
        var command = new FakeRegisterNewUserCommand().Generate() with { PassportNumber = passport };

        await Fixture.SendAsync(command);

        (await Fixture.WaitUntilAsync(() => PassengerExists(passport))).Should().BeTrue();

        var outbox = await PersistMessages(MessageDeliveryType.Outbox);
        var published = outbox.Single(x =>
            x.DataType.Contains(nameof(UserCreated), StringComparison.Ordinal)
            && x.Data.Contains(passport, StringComparison.Ordinal)
        );
        published.MessageStatus.Should().Be(MessageStatus.Processed);

        var inboxId = InboxMessageId.For(PassengerQueue, published.Id);
        (
            await Fixture.WaitUntilAsync(
                async () =>
                    (await PersistMessages(MessageDeliveryType.Inbox)).Any(x =>
                        x.Id == inboxId && x.MessageStatus == MessageStatus.Processed
                    )
            )
        )
            .Should()
            .BeTrue("the passenger consumer records the broker MessageId (= outbox id) in its inbox");

        (await QueueExists(PassengerQueue)).Should().BeTrue();
    }

    [Fact]
    public async Task redelivered_user_created_is_consumed_once()
    {
        var passport = $"D{Random.Shared.Next(100_000_000, 999_999_999)}";
        var messageId = NewId.NextGuid();
        var message = new UserCreated(Guid.NewGuid(), "Duplicate User", passport);
        var publisher = Fixture.ServiceProvider.GetRequiredService<IBus>();

        await publisher.Publish(message, ctx => ctx.MessageId = messageId);
        await publisher.Publish(message, ctx => ctx.MessageId = messageId);

        var inboxId = InboxMessageId.For(PassengerQueue, messageId);
        (
            await Fixture.WaitUntilAsync(
                async () =>
                    (await PersistMessages(MessageDeliveryType.Inbox)).Any(x =>
                        x.Id == inboxId && x.MessageStatus == MessageStatus.Processed
                    )
            )
        )
            .Should()
            .BeTrue();

        (await Fixture.WaitUntilAsync(async () => (await QueueDepth(PassengerQueue)) == 0)).Should().BeTrue();

        (await PersistMessages(MessageDeliveryType.Inbox)).Count(x => x.Id == inboxId).Should().Be(1);
        (await PassengerCount(passport)).Should().Be(1);
    }

    private async Task<bool> PassengerExists(string passport) => await PassengerCount(passport) > 0;

    private async Task<int> PassengerCount(string passport)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PassengerDbContext>();
        return await db.Passengers.CountAsync(x => x.PassportNumber.Value == passport);
    }

    private async Task<PersistMessage[]> PersistMessages(MessageDeliveryType type)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext>();
        return await db.PersistMessage.AsNoTracking().Where(x => x.DeliveryType == type).ToArrayAsync();
    }

    private async Task<bool> QueueExists(string queue) => (await GetQueueJson(queue)) is not null;

    private async Task<int> QueueDepth(string queue)
    {
        var json = await GetQueueJson(queue);
        return json is { } q && q.TryGetProperty("messages", out var m) ? m.GetInt32() : -1;
    }

    private async Task<System.Text.Json.JsonElement?> GetQueueJson(string queue)
    {
        var container = Fixture.RabbitMqTestContainer;
        var options = TestContainers.RabbitMqContainerConfiguration;
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(options.ApiPort)}/"),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.UserName}:{options.Password}"))
        );

        var response = await client.GetAsync($"api/queues/%2F/{Uri.EscapeDataString(queue)}");
        if (!response.IsSuccessStatusCode)
            return null;

        return System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
