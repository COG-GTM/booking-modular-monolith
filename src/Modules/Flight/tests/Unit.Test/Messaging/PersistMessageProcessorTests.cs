namespace Unit.Test.Messaging;

using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

public record OutboxUserCreated(Guid Id, string Name) : IEvent;

public class PersistMessageProcessorTests : IAsyncLifetime
{
    private readonly PersistMessageDbContext _dbContext = new(
        new DbContextOptionsBuilder<PersistMessageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
    );

    private ServiceProvider _provider = null!;
    private ITestHarness _harness = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMassTransitTestHarness();
        _provider = services.BuildServiceProvider(true);
        _harness = _provider.GetRequiredService<ITestHarness>();
        _harness.TestInactivityTimeout = TimeSpan.FromSeconds(2);
        await _harness.Start();
    }

    public async Task DisposeAsync()
    {
        await _harness.Stop();
        await _provider.DisposeAsync();
        await _dbContext.Database.EnsureDeletedAsync();
        await _dbContext.DisposeAsync();
    }

    private PersistMessageProcessor CreateProcessor() =>
        new(NullLogger<PersistMessageProcessor>.Instance, Substitute.For<IMediator>(), _dbContext, _harness.Bus);

    [Fact]
    public async Task add_received_message_with_explicit_id_should_store_an_in_progress_inbox_row_under_that_id()
    {
        var inboxId = Guid.NewGuid();
        var envelope = new MessageEnvelope(new OutboxUserCreated(Guid.NewGuid(), "sam"));

        var id = await CreateProcessor().AddReceivedMessageAsync(inboxId, envelope);

        id.Should().Be(inboxId);
        var row = await _dbContext.PersistMessage.SingleAsync();
        row.Id.Should().Be(inboxId);
        row.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
        row.MessageStatus.Should().Be(MessageStatus.InProgress);
        row.DataType.Should().Be(typeof(OutboxUserCreated).ToString());
    }

    [Fact]
    public async Task add_received_message_without_explicit_id_should_still_generate_an_id()
    {
        var envelope = new MessageEnvelope(new OutboxUserCreated(Guid.NewGuid(), "sam"));

        var id = await CreateProcessor().AddReceivedMessageAsync(envelope);

        id.Should().NotBe(Guid.Empty);
        (await _dbContext.PersistMessage.SingleAsync()).Id.Should().Be(id);
    }

    [Fact]
    public async Task process_inbox_should_mark_the_explicit_id_row_as_processed()
    {
        var inboxId = Guid.NewGuid();
        var processor = CreateProcessor();
        await processor.AddReceivedMessageAsync(
            inboxId,
            new MessageEnvelope(new OutboxUserCreated(Guid.NewGuid(), "sam"))
        );

        await processor.ProcessInboxAsync(inboxId);

        (await _dbContext.PersistMessage.SingleAsync(x => x.Id == inboxId))
            .MessageStatus.Should()
            .Be(MessageStatus.Processed);
        (await processor.ExistMessageAsync(inboxId)).Should().NotBeNull();
        (await processor.GetByFilterAsync(x => x.Id == inboxId && x.DeliveryType == MessageDeliveryType.Inbox))
            .Should()
            .ContainSingle()
            .Which.MessageStatus.Should()
            .Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task processing_an_outbox_row_should_publish_with_the_row_id_as_broker_message_id_and_its_headers()
    {
        var processor = CreateProcessor();
        var @event = new OutboxUserCreated(Guid.NewGuid(), "sam");
        await processor.PublishMessageAsync(
            new MessageEnvelope(@event, new Dictionary<string, object?> { ["CorrelationId"] = "corr-1" })
        );
        var row = await _dbContext.PersistMessage.SingleAsync(x => x.DeliveryType == MessageDeliveryType.Outbox);

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        (await _harness.Published.Any<OutboxUserCreated>()).Should().BeTrue();
        var published = _harness.Published.Select<OutboxUserCreated>().Single();
        published.Context.MessageId.Should().Be(row.Id);
        published.Context.Message.Should().Be(@event);
        published.Context.Headers.Get<string>("CorrelationId").Should().Be("corr-1");
        (await _dbContext.PersistMessage.SingleAsync(x => x.Id == row.Id))
            .MessageStatus.Should()
            .Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task reprocessing_the_same_outbox_row_should_reuse_the_same_broker_message_id()
    {
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var processor = new PersistMessageProcessor(
            NullLogger<PersistMessageProcessor>.Instance,
            Substitute.For<IMediator>(),
            _dbContext,
            publishEndpoint
        );
        await processor.PublishMessageAsync(new MessageEnvelope(new OutboxUserCreated(Guid.NewGuid(), "sam")));
        var row = await _dbContext.PersistMessage.SingleAsync(x => x.DeliveryType == MessageDeliveryType.Outbox);

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);
        row.ChangeState(MessageStatus.InProgress);
        await _dbContext.SaveChangesAsync();
        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        var publishPipes = publishEndpoint
            .ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IPublishEndpoint.Publish))
            .Select(c => c.GetArguments().OfType<IPipe<PublishContext>>().Single())
            .ToList();
        publishPipes.Should().HaveCount(2);

        foreach (var pipe in publishPipes)
        {
            var context = Substitute.For<PublishContext>();
            context.Headers.Returns(Substitute.For<SendHeaders>());

            await pipe.Send(context);

            context.Received(1).MessageId = row.Id;
        }
    }
}
