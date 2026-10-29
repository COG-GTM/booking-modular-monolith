using System.Text.Json;
using BuildingBlocks.Contracts.EventBus.Messages;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contracts.Messages.Tests;

// Identity publishes UserCreated, Passenger consumes it: prove the packaged contract round-trips over MassTransit.
public class UserCreatedContractTests
{
    [Fact]
    public async Task user_created_published_by_identity_is_consumed_by_passenger()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<PassengerUserCreatedConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var sent = new UserCreated(Guid.NewGuid(), "Sam Hamed", "1234567890");
        await harness.Bus.Publish(sent);

        var consumer = harness.GetConsumerHarness<PassengerUserCreatedConsumer>();
        (await consumer.Consumed.Any<UserCreated>()).Should().BeTrue();
        var received = consumer.Consumed.Select<UserCreated>().Single().Context;
        received.Message.Should().Be(sent);
        received.DestinationAddress.Should().NotBeNull();
        MessageUrn.ForTypeString<UserCreated>().Should().Be("urn:message:BuildingBlocks.Contracts.EventBus.Messages:UserCreated");
    }

    [Fact]
    public void v1_wire_payload_still_deserializes()
    {
        const string v1Payload = """{"id":"6f1a2c3d-0000-4000-8000-000000000001","name":"Sam Hamed","passportNumber":"1234567890"}""";

        var message = JsonSerializer.Deserialize<UserCreated>(v1Payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        message.Should().Be(new UserCreated(Guid.Parse("6f1a2c3d-0000-4000-8000-000000000001"), "Sam Hamed", "1234567890"));
        JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)).Should().Be(v1Payload);
    }

    public sealed class PassengerUserCreatedConsumer : IConsumer<UserCreated>
    {
        public Task Consume(ConsumeContext<UserCreated> context) => Task.CompletedTask;
    }
}
