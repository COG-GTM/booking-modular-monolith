using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Messaging.Integration.Test;

public class BrokerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder().WithImage("postgres:16").Build();
    public RabbitMqContainer Rabbit { get; } =
        new RabbitMqBuilder().WithImage("rabbitmq:3-management").WithPortBinding(15672, true).Build();

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await Task.WhenAll(Postgres.StartAsync(), Rabbit.StartAsync());
        await using var db = new PersistMessageDbContext(
            new DbContextOptionsBuilder<PersistMessageDbContext>()
                .UseNpgsql(Postgres.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .Options
        );
        await db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await Rabbit.DisposeAsync();
        await Postgres.DisposeAsync();
    }

    public async Task<ServiceProvider> StartBus(
        string prefix,
        ProbeState state,
        bool connectionString = true,
        bool registerConsumers = true
    )
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Integration");
        var settings = connectionString
            ? new Dictionary<string, string?> { ["ConnectionStrings:rabbitmq"] = Rabbit.GetConnectionString() }
            : new Dictionary<string, string?>
            {
                ["RabbitMqOptions:HostName"] = Rabbit.Hostname,
                ["RabbitMqOptions:Port"] = Rabbit.GetMappedPublicPort(5672).ToString(),
                ["RabbitMqOptions:UserName"] = "rabbitmq",
                ["RabbitMqOptions:Password"] = "rabbitmq",
            };
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(state);
        services.AddSingleton(Substitute.For<IMediator>());
        services.AddDbContext<PersistMessageDbContext>(options =>
            options.UseNpgsql(Postgres.GetConnectionString()).UseSnakeCaseNamingConvention()
        );
        services.AddScoped<IPersistMessageDbContext>(sp => sp.GetRequiredService<PersistMessageDbContext>());
        services.AddScoped<IPersistMessageProcessor, PersistMessageProcessor>();
        services.AddCustomMassTransit(
            env,
            new ConfigurationBuilder().AddInMemoryCollection(settings).Build(),
            prefix,
            registerConsumers ? [typeof(ProbeConsumer).Assembly] : []
        );
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IBusControl>().StartAsync();
        return provider;
    }

    public async Task WaitForQueue(string queue, int delivered = 0, int ready = 0)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("rabbitmq:rabbitmq"))
        );
        await Eventually(async () =>
        {
            using var response = await client.GetAsync(
                $"http://{Rabbit.Hostname}:{Rabbit.GetMappedPublicPort(15672)}/api/queues/%2F/{queue}"
            );
            if (!response.IsSuccessStatusCode)
                return false;
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var deliveries =
                json.TryGetProperty("message_stats", out var stats)
                && stats.TryGetProperty("deliver_get", out var count)
                    ? count.GetInt32()
                    : 0;
            return json.TryGetProperty("messages", out var messages)
                && messages.GetInt32() == ready
                && deliveries >= delivered;
        });
    }

    public static async Task Eventually(Func<Task<bool>> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        while (!await assertion())
            await Task.Delay(100, timeout.Token);
    }
}

public class RabbitMqMessagingTests(BrokerFixture fixture) : IClassFixture<BrokerFixture>
{
    [Fact]
    public async Task Outbox_publishes_stable_message_id_and_replay_is_deduplicated()
    {
        var prefix = "outbox-" + Guid.NewGuid().ToString("N");
        var state = new ProbeState();
        await using var provider = await fixture.StartBus(prefix, state);
        var message = new ProbeEvent(Guid.NewGuid());
        await using (var scope = provider.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor>();
            await processor.PublishMessageAsync(new MessageEnvelope(message));
            await processor.ProcessAsync(message.EventId, MessageDeliveryType.Outbox);
        }
        await fixture.WaitForQueue(prefix + "-probe", 1);
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext>();
            var row = await db.PersistMessage.SingleAsync(x => x.Id == message.EventId);
            Assert.Equal(MessageStatus.Processed, row.MessageStatus);
            Assert.True(await db.InboxMessages.AnyAsync(x => x.MessageId == message.EventId));
            row.ChangeState(MessageStatus.InProgress);
            await db.SaveChangesAsync();
            await scope
                .ServiceProvider.GetRequiredService<IPersistMessageProcessor>()
                .ProcessAsync(message.EventId, MessageDeliveryType.Outbox);
        }
        await fixture.WaitForQueue(prefix + "-probe", 2);
        Assert.Equal(1, state.Attempts[message.EventId]);
        await provider.GetRequiredService<IBusControl>().StopAsync();
    }

    [Fact]
    public async Task Transient_failure_retries_before_recording_receipt()
    {
        var prefix = "retry-" + Guid.NewGuid().ToString("N");
        var state = new ProbeState();
        await using var provider = await fixture.StartBus(prefix, state, connectionString: false);
        var message = new ProbeEvent(Guid.NewGuid(), FailAttempts: 2);
        await provider.GetRequiredService<IBus>().Publish(message);
        await BrokerFixture.Eventually(() => Task.FromResult(state.Completed.ContainsKey(message.EventId)));
        Assert.Equal(3, state.Attempts[message.EventId]);
        await fixture.WaitForQueue(prefix + "-probe", 1);
        await using var scope = provider.CreateAsyncScope();
        Assert.True(
            await scope
                .ServiceProvider.GetRequiredService<PersistMessageDbContext>()
                .InboxMessages.AnyAsync(x => x.MessageId == state.MessageIds[message.EventId])
        );
        await provider.GetRequiredService<IBusControl>().StopAsync();
    }

    [Fact]
    public async Task Exhausted_retries_route_to_error_queue_without_receipt()
    {
        var prefix = "error-" + Guid.NewGuid().ToString("N");
        var state = new ProbeState();
        await using var provider = await fixture.StartBus(prefix, state);
        var message = new ProbeEvent(Guid.NewGuid(), FailAttempts: int.MaxValue);
        await provider.GetRequiredService<IBus>().Publish(message);
        await fixture.WaitForQueue(prefix + "-probe_error", ready: 1);
        Assert.Equal(4, state.Attempts[message.EventId]);
        await using var scope = provider.CreateAsyncScope();
        Assert.False(
            await scope
                .ServiceProvider.GetRequiredService<PersistMessageDbContext>()
                .InboxMessages.AnyAsync(x => x.MessageId == state.MessageIds[message.EventId])
        );
        await provider.GetRequiredService<IBusControl>().StopAsync();
    }

    [Fact]
    public async Task Concurrent_duplicates_are_serialized_but_distinct_message_ids_are_not_event_deduplicated()
    {
        var prefix = "duplicate-" + Guid.NewGuid().ToString("N");
        var state = new ProbeState();
        await using var provider = await fixture.StartBus(prefix, state);
        var message = new ProbeEvent(Guid.NewGuid(), DelayMilliseconds: 200);
        var messageId = Guid.NewGuid();
        var bus = provider.GetRequiredService<IBus>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => bus.Publish(message, x => x.MessageId = messageId)));
        await fixture.WaitForQueue(prefix + "-probe", 8);
        Assert.Equal(1, state.Attempts[message.EventId]);
        await bus.Publish(message, x => x.MessageId = Guid.NewGuid());
        await fixture.WaitForQueue(prefix + "-probe", 9);
        Assert.Equal(2, state.Attempts[message.EventId]);
        await provider.GetRequiredService<IBusControl>().StopAsync();
    }

    [Fact]
    public async Task Service_endpoints_receive_independently_and_receipts_survive_bus_restart()
    {
        var prefix = "services-" + Guid.NewGuid().ToString("N");
        var first = new ProbeState();
        var second = new ProbeState();
        await using var a = await fixture.StartBus(prefix + "-a", first);
        await using var b = await fixture.StartBus(prefix + "-b", second);
        var message = new ProbeEvent(Guid.NewGuid());
        var messageId = Guid.NewGuid();
        await a.GetRequiredService<IBus>().Publish(message, x => x.MessageId = messageId);
        await fixture.WaitForQueue(prefix + "-a-probe", 1);
        await fixture.WaitForQueue(prefix + "-b-probe", 1);
        Assert.Equal(1, first.Attempts[message.EventId]);
        Assert.Equal(1, second.Attempts[message.EventId]);
        await a.GetRequiredService<IBusControl>().StopAsync();
        await b.GetRequiredService<IBusControl>().StopAsync();
        var restarted = new ProbeState();
        await using var c = await fixture.StartBus(prefix + "-a", restarted);
        await c.GetRequiredService<IBus>().Publish(message, x => x.MessageId = messageId);
        await fixture.WaitForQueue(prefix + "-a-probe", 2);
        Assert.Empty(restarted.Attempts);
        await c.GetRequiredService<IBusControl>().StopAsync();
    }
}

public class PublisherStartupTests(BrokerFixture fixture) : IClassFixture<BrokerFixture>
{
    [Fact]
    public async Task Unrouted_outbox_stays_pending_until_the_first_consumer_starts()
    {
        var state = new ProbeState();
        await using var publisher = await fixture.StartBus("publisher", state, registerConsumers: false);
        var message = new ProbeEvent(Guid.NewGuid());
        await using (var scope = publisher.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor>();
            await processor.PublishMessageAsync(new MessageEnvelope(message));
            await Assert.ThrowsAnyAsync<System.Exception>(
                () => processor.ProcessAsync(message.EventId, MessageDeliveryType.Outbox)
            );
            var db = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext>();
            Assert.Equal(MessageStatus.InProgress, (await db.PersistMessage.SingleAsync()).MessageStatus);
        }

        await using var consumer = await fixture.StartBus("late-passenger", state);
        await using (var scope = publisher.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor>().ProcessAllAsync();
            var db = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext>();
            Assert.Equal(MessageStatus.Processed, (await db.PersistMessage.SingleAsync()).MessageStatus);
        }
        await fixture.WaitForQueue("late-passenger-probe", 1);
        Assert.Equal(1, state.Attempts[message.EventId]);
        Assert.Equal(message.EventId, state.MessageIds[message.EventId]);
        await using (var scope = consumer.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext>();
            Assert.True(await db.InboxMessages.AnyAsync(x => x.MessageId == message.EventId));
        }
        await consumer.GetRequiredService<IBusControl>().StopAsync();
        await publisher.GetRequiredService<IBusControl>().StopAsync();
    }
}

public record ProbeEvent(Guid EventId, int FailAttempts = 0, int DelayMilliseconds = 0) : IIntegrationEvent;

public class ProbeState
{
    public ConcurrentDictionary<Guid, int> Attempts { get; } = new();
    public ConcurrentDictionary<Guid, bool> Completed { get; } = new();
    public ConcurrentDictionary<Guid, Guid> MessageIds { get; } = new();
}

public class ProbeConsumer(ProbeState state) : IConsumer<ProbeEvent>
{
    public async Task Consume(ConsumeContext<ProbeEvent> context)
    {
        state.MessageIds[context.Message.EventId] = context.MessageId!.Value;
        var attempt = state.Attempts.AddOrUpdate(context.Message.EventId, 1, (_, n) => n + 1);
        if (attempt <= context.Message.FailAttempts)
            throw new InvalidOperationException("Transient broker test failure");
        await Task.Delay(context.Message.DelayMilliseconds);
        state.Completed[context.Message.EventId] = true;
    }
}
