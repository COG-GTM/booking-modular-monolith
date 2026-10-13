namespace Unit.Test.Messaging;

using BuildingBlocks.MassTransit;
using FluentAssertions;
using Xunit;

public class InboxMessageIdTests
{
    [Fact]
    public void should_be_deterministic_for_the_same_message_id_and_input_address()
    {
        var messageId = Guid.NewGuid();
        var address = new Uri("rabbitmq://localhost/passenger-register-new-user-handler");

        var first = InboxMessageId.Create(messageId, address);
        var second = InboxMessageId.Create(messageId, new Uri(address.ToString()));

        first.Should().Be(second);
        first.Should().NotBe(messageId);
        first.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void should_differ_for_different_message_ids_on_the_same_queue()
    {
        var address = new Uri("rabbitmq://localhost/passenger-register-new-user-handler");

        var first = InboxMessageId.Create(Guid.NewGuid(), address);
        var second = InboxMessageId.Create(Guid.NewGuid(), address);

        first.Should().NotBe(second);
    }

    [Fact]
    public void should_differ_for_the_same_message_delivered_to_different_queues()
    {
        var messageId = Guid.NewGuid();

        var passenger = InboxMessageId.Create(
            messageId,
            new Uri("rabbitmq://localhost/passenger-register-new-user-handler")
        );
        var booking = InboxMessageId.Create(
            messageId,
            new Uri("rabbitmq://localhost/booking-register-new-user-handler")
        );

        passenger.Should().NotBe(booking);
    }

    [Fact]
    public void should_ignore_host_and_scheme_and_only_scope_by_queue_name()
    {
        var messageId = Guid.NewGuid();

        var rabbit = InboxMessageId.Create(messageId, new Uri("rabbitmq://rabbitmq:5672/passenger-handler"));
        var loopback = InboxMessageId.Create(messageId, new Uri("loopback://localhost/passenger-handler"));

        rabbit.Should().Be(loopback);
    }

    [Fact]
    public void should_handle_a_missing_input_address()
    {
        var messageId = Guid.NewGuid();

        var withoutAddress = InboxMessageId.Create(messageId, null);
        var withRootAddress = InboxMessageId.Create(messageId, new Uri("rabbitmq://localhost/"));

        withoutAddress.Should().NotBe(Guid.Empty);
        withoutAddress.Should().Be(withRootAddress);
        withoutAddress.Should().NotBe(InboxMessageId.Create(messageId, new Uri("rabbitmq://localhost/queue")));
    }
}
