using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Unit.Test.Messaging;

public class PersistMessageProcessorInboxTests : IDisposable
{
    public record ProbeEvent(string Value) : IEvent;

    private readonly PersistMessageDbContext _dbContext;
    private readonly PersistMessageProcessor _processor;

    public PersistMessageProcessorInboxTests()
    {
        var options = new DbContextOptionsBuilder<PersistMessageDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PersistMessageDbContext(options);
        _processor = new PersistMessageProcessor(
            NullLogger<PersistMessageProcessor>.Instance,
            Substitute.For<IMediator>(),
            _dbContext,
            Substitute.For<IPublishEndpoint>()
        );
    }

    [Fact]
    public async Task add_received_message_with_a_message_id_should_key_the_inbox_row_on_that_id()
    {
        var messageId = NewId.NextGuid();

        var id = await _processor.AddReceivedMessageAsync(new MessageEnvelope(new ProbeEvent("a")), messageId);

        id.Should().Be(messageId);
        var row = await _dbContext.PersistMessage.SingleAsync();
        row.Id.Should().Be(messageId);
        row.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
        row.MessageStatus.Should().Be(MessageStatus.InProgress);
        row.DataType.Should().Be(typeof(ProbeEvent).ToString());
    }

    [Fact]
    public async Task add_received_message_without_a_message_id_should_still_generate_an_id()
    {
        var id = await _processor.AddReceivedMessageAsync(new MessageEnvelope(new ProbeEvent("a")));

        id.Should().NotBe(Guid.Empty);
        (await _dbContext.PersistMessage.SingleAsync()).Id.Should().Be(id);
    }

    [Fact]
    public async Task process_inbox_should_mark_the_row_with_that_message_id_processed()
    {
        var messageId = NewId.NextGuid();
        await _processor.AddReceivedMessageAsync(new MessageEnvelope(new ProbeEvent("a")), messageId);

        await _processor.ProcessInboxAsync(messageId);

        var rows = await _processor.GetByFilterAsync(x =>
            x.Id == messageId && x.DeliveryType == MessageDeliveryType.Inbox
        );
        rows.Should().ContainSingle().Which.MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task consume_filter_backed_by_the_real_processor_should_run_the_consumer_once_for_a_redelivered_message()
    {
        var messageId = NewId.NextGuid();
        var filter = new ConsumeFilter<ProbeEvent>(_processor);
        var consumed = 0;
        var next = Substitute.For<IPipe<ConsumeContext<ProbeEvent>>>();
        next.Send(Arg.Any<ConsumeContext<ProbeEvent>>())
            .Returns(_ =>
            {
                consumed++;
                return Task.CompletedTask;
            });

        await filter.Send(CreateContext(messageId), next);
        await filter.Send(CreateContext(messageId), next);

        consumed.Should().Be(1);
        var rows = await _dbContext.PersistMessage.ToListAsync();
        rows.Should().ContainSingle().Which.MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task consume_filter_backed_by_the_real_processor_should_treat_different_message_ids_as_different_deliveries()
    {
        var filter = new ConsumeFilter<ProbeEvent>(_processor);
        var consumed = 0;
        var next = Substitute.For<IPipe<ConsumeContext<ProbeEvent>>>();
        next.Send(Arg.Any<ConsumeContext<ProbeEvent>>())
            .Returns(_ =>
            {
                consumed++;
                return Task.CompletedTask;
            });

        await filter.Send(CreateContext(NewId.NextGuid()), next);
        await filter.Send(CreateContext(NewId.NextGuid()), next);

        consumed.Should().Be(2);
        (await _dbContext.PersistMessage.CountAsync()).Should().Be(2);
    }

    public void Dispose()
    {
        _dbContext.Database.EnsureDeleted();
        _dbContext.Dispose();
    }

    private static ConsumeContext<ProbeEvent> CreateContext(Guid messageId)
    {
        var context = Substitute.For<ConsumeContext<ProbeEvent>>();
        context.MessageId.Returns(messageId);
        context.Message.Returns(new ProbeEvent("payload"));
        context.Headers.Returns(Substitute.For<Headers>());
        return context;
    }
}
