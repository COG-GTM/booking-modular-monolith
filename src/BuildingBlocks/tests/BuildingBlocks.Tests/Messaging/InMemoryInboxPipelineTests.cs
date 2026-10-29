using System.Collections.Concurrent;
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
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using PersistMessageProcessorService = BuildingBlocks.PersistMessageProcessor.PersistMessageProcessor<BuildingBlocks.Tests.Messaging.MessagingTestModule>;

namespace BuildingBlocks.Tests.Messaging;

public class InMemoryInboxPipelineTests
{
    [Fact]
    public async Task duplicate_delivery_is_consumed_once_and_stored_as_processed()
    {
        var runId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        InboxProbeHandler.Attempts.TryRemove(runId, out _);
        await using var provider = CreateProvider();
        var busControl = provider.GetRequiredService<IBusControl>();
        using var startTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await busControl.StartAsync(startTimeout.Token);

        try
        {
            var observer = new ReceiveCountObserver(2);
            using var observerHandle = provider.GetRequiredService<IBus>().ConnectReceiveObserver(observer);
            var bus = provider.GetRequiredService<IBus>();

            await bus.Publish(new InboxProbe(runId), context => context.MessageId = messageId);
            await bus.Publish(new InboxProbe(runId), context => context.MessageId = messageId);
            using var receiveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await observer.WaitAsync(receiveTimeout.Token);

            InboxProbeHandler.Attempts[runId].Should().Be(1);
            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<MessagingTestModule>>();
            var stored = await dbContext.PersistMessage.ToListAsync();

            stored.Should().ContainSingle();
            stored[0].Id.Should().Be(InboxMessageId.For("buildingblocks-tests.inbox-probe-handler", messageId));
            stored[0].MessageStatus.Should().Be(MessageStatus.Processed);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await busControl.StopAsync(stopTimeout.Token);
        }
    }

    [Fact]
    public async Task transient_handler_failure_retries_and_processes_one_inbox_row()
    {
        var runId = Guid.NewGuid();
        InboxRetryProbeHandler.Attempts.TryRemove(runId, out _);
        await using var provider = CreateProvider();
        var busControl = provider.GetRequiredService<IBusControl>();
        using var startTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await busControl.StartAsync(startTimeout.Token);

        try
        {
            await provider.GetRequiredService<IBus>().Publish(new InboxRetryProbe(runId, FailuresBeforeSuccess: 1));
            using var deliveryTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await EventuallyAsync(
                async () =>
                {
                    if (InboxRetryProbeHandler.Attempts.GetValueOrDefault(runId) != 2)
                        return false;

                    await using var scope = provider.CreateAsyncScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<
                        PersistMessageDbContext<MessagingTestModule>
                    >();
                    return await dbContext.PersistMessage.AnyAsync(message =>
                        message.MessageStatus == MessageStatus.Processed
                    );
                },
                deliveryTimeout.Token
            );

            await using var scope = provider.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<MessagingTestModule>>();
            var stored = await dbContext.PersistMessage.ToListAsync();

            InboxRetryProbeHandler.Attempts[runId].Should().Be(2);
            stored.Should().ContainSingle();
            stored[0].MessageStatus.Should().Be(MessageStatus.Processed);
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await busControl.StopAsync(stopTimeout.Token);
        }
    }

    [Fact]
    public void consumers_are_scanned_only_from_explicit_assemblies()
    {
        var configuration = Configuration();
        var env = Environment();
        var emptyServices = new ServiceCollection();
        emptyServices.AddLogging();
        emptyServices.AddSingleton<IConfiguration>(configuration);
        emptyServices.AddCustomMassTransit(configuration, env, configureBus: null);

        emptyServices.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IConsumerRegistration));

        var scannedServices = new ServiceCollection();
        scannedServices.AddLogging();
        scannedServices.AddSingleton<IConfiguration>(configuration);
        scannedServices.AddCustomMassTransit(
            configuration,
            env,
            configureBus: null,
            typeof(InboxProbeHandler).Assembly
        );

        scannedServices
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IConsumerRegistration)
                && RegistrationIncludes(descriptor, typeof(InboxProbeHandler))
            );
    }

    private static ServiceProvider CreateProvider()
    {
        var configuration = Configuration();
        var databaseName = "in-memory-inbox-" + Guid.NewGuid();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDbContext<PersistMessageDbContext<MessagingTestModule>>(options =>
            options.UseInMemoryDatabase(databaseName)
        );
        services.AddScoped<IPersistMessageDbContext<MessagingTestModule>>(provider =>
            provider.GetRequiredService<PersistMessageDbContext<MessagingTestModule>>()
        );
        services.AddScoped<IPersistMessageProcessor<MessagingTestModule>, PersistMessageProcessorService>();
        services.AddSingleton(Substitute.For<IMediator>());
        services.AddCustomMassTransit(
            configuration,
            Environment(),
            (context, bus) => bus.UsePersistMessageInbox(context, typeof(MessagingInboxFilter<>)),
            typeof(InboxProbeHandler).Assembly
        );

        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["MessagingOptions:Transport"] = "InMemory" })
            .Build();

    private static IWebHostEnvironment Environment()
    {
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("inbox-pipeline");
        return env;
    }

    private static bool RegistrationIncludes(ServiceDescriptor descriptor, Type consumerType)
    {
        var implementationType = descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
        return implementationType?.IsGenericType == true
            && implementationType.GetGenericArguments().Contains(consumerType);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> predicate, CancellationToken cancellationToken)
    {
        while (!await predicate())
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
    }

    private sealed class ReceiveCountObserver(int expectedCount) : IReceiveObserver
    {
        private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _count;

        public Task PreReceive(ReceiveContext context) => Task.CompletedTask;

        public Task PostReceive(ReceiveContext context)
        {
            if (Interlocked.Increment(ref _count) >= expectedCount)
                _received.TrySetResult();

            return Task.CompletedTask;
        }

        public Task PostConsume<T>(ConsumeContext<T> context, TimeSpan duration, string consumerType)
            where T : class => Task.CompletedTask;

        public Task ConsumeFault<T>(
            ConsumeContext<T> context,
            TimeSpan duration,
            string consumerType,
            System.Exception exception
        )
            where T : class => Task.CompletedTask;

        public Task ReceiveFault(ReceiveContext context, System.Exception exception) => Task.CompletedTask;

        public Task WaitAsync(CancellationToken cancellationToken) => _received.Task.WaitAsync(cancellationToken);
    }
}

public record InboxProbe(Guid RunId);

public class InboxProbeHandler : IConsumer<InboxProbe>
{
    public static readonly ConcurrentDictionary<Guid, int> Attempts = new();

    public Task Consume(ConsumeContext<InboxProbe> context)
    {
        Attempts.AddOrUpdate(context.Message.RunId, 1, (_, attempts) => attempts + 1);
        return Task.CompletedTask;
    }
}

public record InboxRetryProbe(Guid RunId, int FailuresBeforeSuccess);

public class InboxRetryProbeHandler : IConsumer<InboxRetryProbe>
{
    public static readonly ConcurrentDictionary<Guid, int> Attempts = new();

    public Task Consume(ConsumeContext<InboxRetryProbe> context)
    {
        var attempt = Attempts.AddOrUpdate(context.Message.RunId, 1, (_, attempts) => attempts + 1);
        if (attempt <= context.Message.FailuresBeforeSuccess)
            throw new InvalidOperationException($"transient failure {attempt}");

        return Task.CompletedTask;
    }
}
