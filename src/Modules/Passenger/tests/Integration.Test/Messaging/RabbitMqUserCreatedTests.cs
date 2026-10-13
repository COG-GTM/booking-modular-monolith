using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using EasyNetQ.Management.Client;
using FluentAssertions;
using Identity.Identity.Models;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Xunit;
using RegisterNewUserCommand = Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUser;

namespace Integration.Test.Messaging;

// Runs the monolith on a real RabbitMQ (Testcontainers) instead of the in-memory transport.
public class RabbitMqTestFixture : TestFixture<Program, PassengerDbContext, PassengerReadDbContext>
{
    public const string ServiceName = "passenger-it";

    protected override IEnumerable<KeyValuePair<string, string?>> AdditionalAppSettings() =>
        [
            new("MessageBroker:TransportType", nameof(TransportType.RabbitMq)),
            new("MessageBroker:ServiceName", ServiceName),
            new("MessageBroker:UseInbox", "true"),
        ];

    public ManagementClient CreateManagementClient() =>
        new(
            RabbitMqTestContainer.Hostname,
            TestContainers.RabbitMqContainerConfiguration.UserName,
            TestContainers.RabbitMqContainerConfiguration.Password,
            RabbitMqTestContainer.GetMappedPublicPort(TestContainers.RabbitMqContainerConfiguration.ApiPort)
        );
}

[CollectionDefinition(Name)]
public class RabbitMqTestCollection : ICollectionFixture<RabbitMqTestFixture>
{
    public const string Name = "Passenger RabbitMQ Integration Test";
}

[Collection(RabbitMqTestCollection.Name)]
public class RabbitMqUserCreatedTests : TestBase<Program, PassengerDbContext, PassengerReadDbContext>
{
    private const string ConsumerQueue = $"{RabbitMqTestFixture.ServiceName}-register-new-user-handler";
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly RabbitMqTestFixture _fixture;

    public RabbitMqUserCreatedTests(RabbitMqTestFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task bus_should_use_rabbitmq_with_service_prefixed_consumer_queue()
    {
        var bus = _fixture.ServiceProvider.GetRequiredService<IBus>();
        bus.Address.Scheme.Should().Be("rabbitmq");

        var queues = await _fixture.CreateManagementClient().GetQueuesAsync();
        queues.Select(q => q.Name).Should().Contain(ConsumerQueue);
    }

    [Fact]
    public async Task user_created_from_identity_outbox_should_be_consumed_by_passenger_over_rabbitmq()
    {
        var passportNumber = $"P{Random.Shared.Next(10_000_000, 99_999_999)}";
        var command = new RegisterNewUserCommand(
            "Rabbit",
            "Mq",
            $"rabbit{Guid.NewGuid():N}"[..20],
            $"{Guid.NewGuid():N}@test.com",
            "Password@123",
            "Password@123",
            passportNumber
        );

        // The write DB is shared with Identity and reset after every test, so restore the roles it needs.
        await EnsureIdentityRolesAsync();

        await _fixture.SendAsync(command);

        var outboxRow = await WaitForAsync(
            async () =>
                (await PersistMessages(x => x.DeliveryType == MessageDeliveryType.Outbox)).FirstOrDefault(x =>
                    x.DataType.Contains(nameof(UserCreated), StringComparison.Ordinal)
                    && x.Data.Contains(passportNumber, StringComparison.Ordinal)
                )
        );
        outboxRow.Should().NotBeNull("Identity must write UserCreated to its outbox");

        var passengerCreated = await WaitForAsync(
            async () =>
                await _fixture.ExecuteDbContextAsync(db =>
                    db.Passengers.AnyAsync(x => x.PassportNumber.Value == passportNumber)
                )
                    ? true
                    : (bool?)null
        );
        passengerCreated.Should().BeTrue("Passenger must consume UserCreated delivered by RabbitMQ");

        var consumed = _fixture
            .ServiceProvider.GetRequiredService<ITestHarness>()
            .Consumed.Select<UserCreated>(x => x.Context.MessageId == outboxRow!.Id)
            .FirstOrDefault();
        consumed.Should().NotBeNull("the outbox row id is the broker MessageId");
        consumed!.Context.ReceiveContext.InputAddress!.AbsolutePath.Trim('/').Should().Be(ConsumerQueue);

        var inboxId = InboxMessageId.Create(outboxRow!.Id, consumed.Context.ReceiveContext.InputAddress);
        var inboxRow = await WaitForAsync(
            async () =>
                (
                    await PersistMessages(x => x.Id == inboxId && x.MessageStatus == MessageStatus.Processed)
                ).FirstOrDefault()
        );
        inboxRow.Should().NotBeNull();
        inboxRow!.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
    }

    [Fact]
    public async Task redelivered_message_with_same_message_id_should_be_processed_once_by_inbox()
    {
        var messageId = NewId.NextGuid();
        var passportNumber = $"D{Random.Shared.Next(10_000_000, 99_999_999)}";
        var message = new UserCreated(Guid.NewGuid(), "Dedup User", passportNumber);
        var inboxId = InboxMessageId.Create(messageId, new Uri($"rabbitmq://localhost/{ConsumerQueue}"));

        var receiveObserver = new MessageIdReceiveObserver(messageId);
        using var observerHandle = _fixture
            .ServiceProvider.GetRequiredService<IBus>()
            .ConnectReceiveObserver(receiveObserver);

        await PublishWithMessageId(message, messageId);

        (
            await WaitForAsync(
                async () =>
                    (
                        await PersistMessages(x => x.Id == inboxId && x.MessageStatus == MessageStatus.Processed)
                    ).FirstOrDefault()
            )
        )
            .Should()
            .NotBeNull();

        await PublishWithMessageId(message, messageId);

        (await WaitForAsync(() => Task.FromResult(receiveObserver.Received >= 2 ? true : (bool?)null)))
            .Should()
            .BeTrue("both deliveries must reach the endpoint");

        var consumerHarness = _fixture.ServiceProvider.GetRequiredService<ITestHarness>();
        var handlerHarness = consumerHarness.GetConsumerHarness<RegisterNewUserHandler>();
        handlerHarness
            .Consumed.Select<UserCreated>(x => x.Context.MessageId == messageId)
            .Should()
            .HaveCount(1, "the inbox must short-circuit the duplicate before the consumer runs");

        (await PersistMessages(x => x.DeliveryType == MessageDeliveryType.Inbox && x.Id == inboxId))
            .Should()
            .ContainSingle();
    }

    [Fact]
    public async Task failing_message_should_be_retried_then_moved_to_error_queue()
    {
        var messageId = NewId.NextGuid();

        // Empty name makes Passenger throw InvalidNameException (not a ValidationException) on every attempt.
        await PublishWithMessageId(new UserCreated(Guid.NewGuid(), string.Empty, "R12345678"), messageId);

        var managementClient = _fixture.CreateManagementClient();
        var errorQueue = $"{ConsumerQueue}_error";

        var errorMessage = await WaitForAsync(async () =>
        {
            var queues = await managementClient.GetQueuesAsync();
            if (queues.FirstOrDefault(q => q.Name == errorQueue) is not { Messages: > 0 } queue)
            {
                return null;
            }

            var messages = await managementClient.GetMessagesFromQueueAsync(
                queue,
                new EasyNetQ.Management.Client.Model.GetMessagesFromQueueInfo(
                    100,
                    EasyNetQ.Management.Client.Model.AckMode.AckRequeueTrue
                )
            );

            return messages.FirstOrDefault(m =>
                Equals(m.Properties.GetValueOrDefault("message_id"), messageId.ToString())
            );
        });

        errorMessage.Should().NotBeNull("the message must land in the error queue after retries are exhausted");
        var headers = errorMessage!.Properties["headers"] as IReadOnlyDictionary<string, object?>;
        headers.Should().NotBeNull();
        headers!["MT-Fault-RetryCount"]?.ToString().Should().Be("3");

        var inboxId = InboxMessageId.Create(messageId, new Uri($"rabbitmq://localhost/{ConsumerQueue}"));
        (await PersistMessages(x => x.Id == inboxId))
            .Should()
            .ContainSingle()
            .Which.MessageStatus.Should()
            .NotBe(MessageStatus.Processed);
    }

    private async Task EnsureIdentityRolesAsync()
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();

        foreach (var role in new[] { IdentityConstant.Role.Admin, IdentityConstant.Role.User })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new Role { Name = role });
            }
        }
    }

    private async Task PublishWithMessageId(UserCreated message, Guid messageId)
    {
        var bus = _fixture.ServiceProvider.GetRequiredService<IBus>();
        await bus.Publish(message, ctx => ctx.MessageId = messageId);
    }

    private async Task<IReadOnlyList<PersistMessage>> PersistMessages(
        System.Linq.Expressions.Expression<Func<PersistMessage, bool>> predicate
    )
    {
        using var scope = _fixture.ServiceProvider.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor>();
        return await processor.GetByFilterAsync(predicate);
    }

    private static async Task<T?> WaitForAsync<T>(Func<Task<T?>> probe)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (DateTime.UtcNow < deadline)
        {
            var result = await probe();
            if (result is not null)
            {
                return result;
            }

            await Task.Delay(250);
        }

        return default;
    }
}

// Counts every delivery of one MessageId that reaches a receive endpoint, before any consume filter runs.
internal sealed class MessageIdReceiveObserver(Guid messageId) : IReceiveObserver
{
    private int _received;

    public int Received => Volatile.Read(ref _received);

    public Task PreReceive(ReceiveContext context) => Task.CompletedTask;

    public Task PostReceive(ReceiveContext context)
    {
        if (context.GetMessageId() == messageId)
        {
            Interlocked.Increment(ref _received);
        }

        return Task.CompletedTask;
    }

    public Task PostConsume<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType)
        where T : class => Task.CompletedTask;

    public Task ConsumeFault<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType, Exception exception)
        where T : class => Task.CompletedTask;

    public Task ReceiveFault(ReceiveContext context, Exception exception) => Task.CompletedTask;
}
