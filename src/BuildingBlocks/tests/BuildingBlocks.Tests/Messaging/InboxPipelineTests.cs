#pragma warning disable SA1300

using System.Collections.Concurrent;
using System.Reflection;
using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MassTransit.Configuration;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests.Messaging.InboxPipeline;

public class InboxPipelineTests
{
    [Fact]
    public async Task outbox_publish_uses_the_persisted_row_id_as_message_id()
    {
        await using var fixture = await StartBusAsync();
        var correlationId = Guid.NewGuid();
        var @event = new InboxProbeEvent(correlationId);
        var headers = new Dictionary<string, object?> { ["inbox-test-header"] = "persisted-value" };
        var envelope = new MessageEnvelope(@event, headers);

        await using (var scope = fixture.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor>();
            await processor.PublishMessageAsync(envelope);
            await processor.ProcessAllAsync();
        }

        var delivery = await WaitForDeliveryAsync(correlationId);
        var outboxRow = await WaitForMessageAsync(
            fixture.Services,
            message =>
                message.DeliveryType == MessageDeliveryType.Outbox && message.MessageStatus == MessageStatus.Processed
        );

        delivery.MessageId.Should().Be(outboxRow.Id);
        delivery.CustomHeader.Should().Be("persisted-value");

        var endpoint = delivery.InputAddress.AbsolutePath.Trim('/');
        var inboxId = InboxMessageId.For(endpoint, outboxRow.Id);
        inboxId.Should().NotBe(outboxRow.Id);
        var inboxRow = await WaitForMessageAsync(fixture.Services, message => message.Id == inboxId);
        inboxRow.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
        inboxRow.MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public void consumer_from_another_module_assembly_is_not_registered()
    {
        var otherAssembly = CreateMassTransitServices(typeof(ModuleEndpointNameFormatter).Assembly);

        IsProbeConsumerRegistered(otherAssembly).Should().BeFalse();
    }

    private static async Task<InboxBusFixture> StartBusAsync()
    {
        var services = CreateMassTransitServices(typeof(InboxProbeHandler).Assembly);
        var databaseName = $"inbox-test-{Guid.NewGuid():N}";
        services.AddDbContext<PersistMessageDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IPersistMessageDbContext>(provider =>
            provider.GetRequiredService<PersistMessageDbContext>()
        );
        services.AddScoped<IPersistMessageProcessor, BuildingBlocks.PersistMessageProcessor.PersistMessageProcessor>();
        services.AddScoped<IMediator>(_ => Substitute.For<IMediator>());

        var provider = services.BuildServiceProvider();
        var startTimeout = TimeSpan.FromSeconds(30);
        using var startCancellation = new CancellationTokenSource(startTimeout);
        var busStarted = false;
        try
        {
            var bus = provider.GetRequiredService<IBusControl>();
            await bus.StartAsync(startCancellation.Token);
            busStarted = true;
            return new InboxBusFixture(provider, bus);
        }
        finally
        {
            if (!busStarted)
                await provider.DisposeAsync();
        }
    }

    private static ServiceCollection CreateMassTransitServices(params Assembly[] consumerAssemblies)
    {
        var configuration = new ConfigurationBuilder().Build();
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns("inbox-test");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCustomMassTransit(
            environment,
            TransportType.InMemory,
            (context, bus) => bus.UsePersistMessageInbox(context),
            consumerAssemblies
        );

        return services;
    }

    private static bool IsProbeConsumerRegistered(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();
        return provider
            .GetServices<IConsumerRegistration>()
            .Any(registration => registration.GetType().GetGenericArguments().Contains(typeof(InboxProbeHandler)));
    }

    private static async Task<InboxProbeDelivery> WaitForDeliveryAsync(Guid correlationId)
    {
        var found = await WaitUntilAsync(() => InboxProbeHandler.Deliveries.ContainsKey(correlationId));
        found.Should().BeTrue("the in-memory consumer should receive the published event");
        return InboxProbeHandler.Deliveries[correlationId];
    }

    private static async Task<PersistMessage> WaitForMessageAsync(
        IServiceProvider services,
        Func<PersistMessage, bool> predicate
    )
    {
        PersistMessage? result = null;
        PersistMessage[] observedMessages = [];
        var found = await WaitUntilAsync(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext>();
            var messages = await db.PersistMessage.AsNoTracking().ToListAsync();
            observedMessages = messages.ToArray();
            result = messages.FirstOrDefault(predicate);
            return result is not null;
        });

        var messageSummaries = observedMessages
            .Select(message => $"{message.Id}:{message.DeliveryType}:{message.MessageStatus}")
            .ToArray();
        var observedRows = string.Join(", ", messageSummaries);
        var failureReason = $"the persist-message row should reach the expected state; observed rows: {observedRows}";
        found.Should().BeTrue(failureReason);
        return result!;
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;
            await Task.Delay(25);
        }

        return false;
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(25);
        }

        return false;
    }

    private sealed class InboxBusFixture(ServiceProvider services, IBusControl bus) : IAsyncDisposable
    {
        public ServiceProvider Services { get; } = services;
        public IBusControl Bus { get; } = bus;

        public async ValueTask DisposeAsync()
        {
            await Bus.StopAsync();
            await Services.DisposeAsync();
        }
    }

    public sealed class InboxProbeHandler : IConsumer<InboxProbeEvent>
    {
        public static ConcurrentDictionary<Guid, InboxProbeDelivery> Deliveries { get; } = new();

        public Task Consume(ConsumeContext<InboxProbeEvent> context)
        {
            var correlationId = context.Message.CorrelationId;
            var headers = context.Headers.ToDictionary(header => header.Key, header => header.Value);
            headers.TryGetValue("inbox-test-header", out var headerValue);

            Deliveries[correlationId] = new(
                context.MessageId,
                context.ReceiveContext.InputAddress,
                headerValue?.ToString()
            );
            return Task.CompletedTask;
        }
    }
}

public sealed record InboxProbeEvent(Guid CorrelationId) : IEvent;

public sealed record InboxProbeDelivery(Guid? MessageId, Uri InputAddress, string? CustomHeader);
