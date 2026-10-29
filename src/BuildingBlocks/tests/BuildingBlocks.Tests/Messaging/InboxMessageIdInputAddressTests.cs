#pragma warning disable SA1300

using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class InboxMessageIdInputAddressTests
{
    [Fact]
    public void input_address_absolute_path_is_trimmed_before_hashing()
    {
        var messageId = Guid.NewGuid();
        var inputAddress = new Uri("rabbitmq://localhost/vhost/passenger.q");
        var receiveContext = Substitute.For<ReceiveContext>();
        receiveContext.InputAddress.Returns(inputAddress);
        var context = Substitute.For<ConsumeContext>();
        context.MessageId.Returns(messageId);
        context.ReceiveContext.Returns(receiveContext);
        var expected = InboxMessageId.For("vhost/passenger.q", messageId);

        var actual = InboxMessageId.For(context);

        actual.Should().Be(expected);
    }
}
