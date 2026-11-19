using BuildingBlocks.Contracts.EventBus.Messages;
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
using OutboxProcessor = BuildingBlocks.PersistMessageProcessor.PersistMessageProcessor;

namespace BuildingBlocks.Tests;

public class LegacyOutboxReplayTests
{
    [Fact]
    public async Task outbox_publishes_pre_extraction_user_created_and_marks_it_processed()
    {
        const string legacyType = "BuildingBlocks.Contracts.EventBus.Messages.UserCreated";
        const string legacyEnvelope = """
            {
              "Message": {
                "Id": "6f1a2c3d-0000-4000-8000-000000000001",
                "Name": "Test User",
                "PassportNumber": "P1234567"
              },
              "Headers": {}
            }
            """;
        var options = new DbContextOptionsBuilder<PersistMessageDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PersistMessageDbContext(options);
        var rowId = Guid.NewGuid();
        db.PersistMessage.Add(new PersistMessage(rowId, legacyType, legacyEnvelope, MessageDeliveryType.Outbox));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.SetTestTimeouts(testInactivityTimeout: TimeSpan.FromSeconds(5)))
            .BuildServiceProvider(true);
        var harness = provider.GetTestHarness();
        await harness.Start();
        var expected = new UserCreated(Guid.Parse("6f1a2c3d-0000-4000-8000-000000000001"), "Test User", "P1234567");
        var processor = new OutboxProcessor(
            NullLogger<OutboxProcessor>.Instance,
            Substitute.For<IMediator>(),
            db,
            harness.Bus
        );

        await processor.ProcessAsync(rowId, MessageDeliveryType.Outbox);

        (await harness.Published.Any<UserCreated>()).Should().BeTrue();
        harness.Published.Select<UserCreated>().Single().Context.Message.Should().Be(expected);
        var storedRow = await db.PersistMessage.AsNoTracking().SingleAsync(row => row.Id == rowId);
        storedRow.MessageStatus.Should().Be(MessageStatus.Processed);
    }
}
