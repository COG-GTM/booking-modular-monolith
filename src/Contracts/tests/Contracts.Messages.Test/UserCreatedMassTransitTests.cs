using BuildingBlocks.Contracts.EventBus.Messages;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contracts.Messages.Test;

public class UserCreatedMassTransitTests
{
    [Fact]
    public async Task User_created_is_consumed_with_pinned_envelope_urn()
    {
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(configuration => configuration.AddConsumer<UserCreatedConsumer>())
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var id = Guid.NewGuid();
        var message = new UserCreated(id, "name", "P12345");
        await harness.Bus.Publish(message);

        Assert.True(await harness.Consumed.Any<UserCreated>());
        var consumed = await harness.Consumed.SelectAsync<UserCreated>().FirstOrDefault();
        Assert.NotNull(consumed);
        Assert.Equal(message, consumed.Context.Message);

        var published = await harness.Published.SelectAsync<UserCreated>().FirstOrDefault();
        Assert.NotNull(published);
        Assert.Contains(
            "urn:message:BuildingBlocks.Contracts.EventBus.Messages:UserCreated",
            published.Context.SupportedMessageTypes
        );
    }

    public sealed class UserCreatedConsumer : IConsumer<UserCreated>
    {
        public Task Consume(ConsumeContext<UserCreated> context)
        {
            return Task.CompletedTask;
        }
    }
}
