using BuildingBlocks.MassTransit;
using FluentAssertions;
using MassTransit;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public class InboxMessageIdTests
{
    [Fact]
    public void message_without_message_id_throws()
    {
        var context = Substitute.For<ConsumeContext>();
        context.MessageId.Returns((Guid?)null);

        var act = () => InboxMessageId.For(context);

        act.Should().Throw<InvalidOperationException>().WithMessage("Inbox requires a MessageId.");
    }

    [Fact]
    public void missing_receive_context_uses_an_empty_endpoint()
    {
        var messageId = Guid.NewGuid();
        var context = Substitute.For<ConsumeContext>();
        context.MessageId.Returns(messageId);
        context.ReceiveContext.Returns((ReceiveContext?)null);

        InboxMessageId.For(context).Should().Be(InboxMessageId.For(string.Empty, messageId));
    }

    [Fact]
    public void generated_id_has_version_eight_and_rfc_4122_variant()
    {
        var id = InboxMessageId.For("buildingblocks-tests.probe", Guid.NewGuid());
        var hex = id.ToString("N");

        hex[12].Should().Be('8');
        new[] { '8', '9', 'a', 'b' }.Should().Contain(char.ToLowerInvariant(hex[16]));
    }
}
