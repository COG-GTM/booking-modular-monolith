using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using NSubstitute;
using Xunit;

namespace Unit.Test.PersistMessageProcessor;

public class PersistMessageIntegrationEventPublisherTests
{
    private readonly IPersistMessageProcessor processor = Substitute.For<IPersistMessageProcessor>();

    [Fact]
    public async Task publish_async_should_delegate_to_persist_message_processor()
    {
        var envelope = new MessageEnvelope(new object(), new Dictionary<string, object?>());
        var publisher = new PersistMessageIntegrationEventPublisher(processor);

        await publisher.PublishAsync(envelope);

        await processor.Received(1).PublishMessageAsync(envelope, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task add_internal_message_async_should_delegate_to_persist_message_processor()
    {
        var command = Substitute.For<IInternalCommand>();
        var publisher = new PersistMessageIntegrationEventPublisher(processor);

        await publisher.AddInternalMessageAsync(command);

        await processor.Received(1).AddInternalMessageAsync(command, Arg.Any<CancellationToken>());
    }
}
