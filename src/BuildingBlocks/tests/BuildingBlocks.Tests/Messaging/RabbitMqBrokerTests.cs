using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Testcontainers.RabbitMq;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

// Runs the real AddCustomMassTransit RabbitMQ pipeline against a RabbitMQ Testcontainer.
public sealed class RabbitMqBrokerFixture : IAsyncLifetime
{
    public const string User = "booking";
    public const string Password = "booking";

    public RabbitMqContainer Container { get; } =
        new RabbitMqBuilder()
            .WithImage("rabbitmq:management")
            .WithUsername(User)
            .WithPassword(Password)
            .WithPortBinding(15672, true)
            .Build();

    public ServiceProvider Services { get; private set; } = null!;

    public IBus Bus => Services.GetRequiredService<IBus>();

    public async Task InitializeAsync()
    {
        await Container.StartAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["MessagingOptions:Transport"] = "RabbitMq",
                    ["RabbitMqOptions:HostName"] = Container.Hostname,
                    ["RabbitMqOptions:Port"] = Container
                        .GetMappedPublicPort(5672)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["RabbitMqOptions:UserName"] = User,
                    ["RabbitMqOptions:Password"] = Password,
                }
            )
            .Build();

        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("broker-test");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCustomMassTransit(configuration, env, configureBus: null, typeof(FlakyConsumer).Assembly);

        Services = services.BuildServiceProvider();
        await Services
            .GetRequiredService<IBusControl>()
            .StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
    }

    public async Task DisposeAsync()
    {
        await Services.GetRequiredService<IBusControl>().StopAsync();
        await Services.DisposeAsync();
        await Container.DisposeAsync();
    }

    public async Task<JsonElement?> GetQueueAsync(string name)
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{Container.Hostname}:{Container.GetMappedPublicPort(15672)}/"),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{User}:{Password}"))
        );

        var response = await client.GetAsync($"api/queues/%2F/{Uri.EscapeDataString(name)}");

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}

public class RabbitMqBrokerTests : IClassFixture<RabbitMqBrokerFixture>
{
    private readonly RabbitMqBrokerFixture _broker;

    public RabbitMqBrokerTests(RabbitMqBrokerFixture broker) => _broker = broker;

    [Fact]
    public async Task consumers_bind_module_prefixed_queues_on_the_broker()
    {
        (await _broker.GetQueueAsync("buildingblocks-tests.flaky")).Should().NotBeNull();
        (await _broker.GetQueueAsync("buildingblocks-tests.always-failing")).Should().NotBeNull();
        (await _broker.GetQueueAsync("flaky")).Should().BeNull("queues are owned by the module, not shared by name");
    }

    [Fact]
    public async Task transient_consumer_failures_are_retried_until_they_succeed()
    {
        var id = Guid.NewGuid();

        await _broker.Bus.Publish(new FlakyCommand(id, FailuresBeforeSuccess: 2));

        (await Eventually(() => FlakyConsumer.Succeeded.ContainsKey(id))).Should().BeTrue();
        FlakyConsumer.Attempts[id].Should().Be(3);
    }

    [Fact]
    public async Task exhausted_retries_fault_and_move_the_message_to_the_error_queue()
    {
        var id = Guid.NewGuid();

        await _broker.Bus.Publish(new PoisonEvent(id));

        (await Eventually(() => PoisonFaultConsumer.Faulted.ContainsKey(id))).Should().BeTrue();
        AlwaysFailingConsumer.Attempts[id].Should().Be(4, "one delivery plus three retries");

        (
            await Eventually(
                async () =>
                    (await _broker.GetQueueAsync("buildingblocks-tests.always-failing_error")) is { } q
                    && q.TryGetProperty("messages", out var count)
                    && count.GetInt32() >= 1
            )
        )
            .Should()
            .BeTrue();
    }

    private static Task<bool> Eventually(Func<bool> condition) => Eventually(() => Task.FromResult(condition()));

    private static async Task<bool> Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;

            await Task.Delay(250);
        }

        return false;
    }
}

public record FlakyCommand(Guid Id, int FailuresBeforeSuccess);

public record PoisonEvent(Guid Id);

public class FlakyConsumer : IConsumer<FlakyCommand>
{
    public static readonly ConcurrentDictionary<Guid, int> Attempts = new();
    public static readonly ConcurrentDictionary<Guid, bool> Succeeded = new();

    public Task Consume(ConsumeContext<FlakyCommand> context)
    {
        var attempt = Attempts.AddOrUpdate(context.Message.Id, 1, (_, n) => n + 1);

        if (attempt <= context.Message.FailuresBeforeSuccess)
            throw new InvalidOperationException($"transient failure {attempt}");

        Succeeded[context.Message.Id] = true;
        return Task.CompletedTask;
    }
}

public class AlwaysFailingConsumer : IConsumer<PoisonEvent>
{
    public static readonly ConcurrentDictionary<Guid, int> Attempts = new();

    public Task Consume(ConsumeContext<PoisonEvent> context)
    {
        Attempts.AddOrUpdate(context.Message.Id, 1, (_, n) => n + 1);
        throw new InvalidOperationException("poison message");
    }
}

public class PoisonFaultConsumer : IConsumer<Fault<PoisonEvent>>
{
    public static readonly ConcurrentDictionary<Guid, bool> Faulted = new();

    public Task Consume(ConsumeContext<Fault<PoisonEvent>> context)
    {
        Faulted[context.Message.Message.Id] = true;
        return Task.CompletedTask;
    }
}
