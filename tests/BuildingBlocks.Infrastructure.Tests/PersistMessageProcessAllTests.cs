using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace BuildingBlocks.Infrastructure.Tests;

public class PersistMessageProcessAllTests
{
    [Fact]
    public async Task ProcessAllAsync_continues_after_an_outbox_row_fails_to_publish()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TestModule>>().UseSqlite(connection).Options;
        var mediator = Substitute.For<IMediator>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        publishEndpoint
            .When(endpoint =>
                endpoint.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>())
            )
            .Do(callInfo =>
            {
                if (callInfo.ArgAt<object>(0) is SeatReservedIntegration)
                    throw new InvalidOperationException();
            });

        await using (var seedDb = new PersistMessageDbContext<TestModule>(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            var processor = CreateProcessor(seedDb, mediator, publishEndpoint);
            await processor.PublishMessageAsync(new MessageEnvelope(new SeatReservedIntegration()));
            await processor.PublishMessageAsync(new MessageEnvelope(new SeatReleasedIntegration()));
        }

        await using (var workerDb = new PersistMessageDbContext<TestModule>(options))
        {
            await CreateProcessor(workerDb, mediator, publishEndpoint).ProcessAllAsync();
        }

        var publishedMessages = publishEndpoint.ReceivedCalls().Select(call => call.GetArguments()[0]).ToList();
        Assert.Contains(publishedMessages, message => message is SeatReservedIntegration);
        Assert.Contains(publishedMessages, message => message is SeatReleasedIntegration);

        await using var assertDb = new PersistMessageDbContext<TestModule>(options);
        var rows = await assertDb.PersistMessage.ToListAsync();
        var reserved = Assert.Single(rows, message => message.DataType == typeof(SeatReservedIntegration).ToString());
        var released = Assert.Single(rows, message => message.DataType == typeof(SeatReleasedIntegration).ToString());
        Assert.Equal(MessageStatus.InProgress, reserved.MessageStatus);
        Assert.Equal(MessageStatus.Processed, released.MessageStatus);
    }

    [Fact]
    public async Task ProcessAllAsync_continues_after_an_internal_command_fails()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TestModule>>().UseSqlite(connection).Options;
        var mediator = Substitute.For<IMediator>();
        mediator
            .When(mediator => mediator.Send(Arg.Any<object>(), Arg.Any<CancellationToken>()))
            .Do(callInfo =>
            {
                if (callInfo.ArgAt<object>(0) is ConfirmSeat)
                    throw new InvalidOperationException();
            });
        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        await using (var seedDb = new PersistMessageDbContext<TestModule>(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            var processor = CreateProcessor(seedDb, mediator, publishEndpoint);
            await processor.AddInternalMessageAsync(new ConfirmSeat());
            await processor.PublishMessageAsync(new MessageEnvelope(new SeatReleasedIntegration()));
        }

        await using (var workerDb = new PersistMessageDbContext<TestModule>(options))
        {
            await CreateProcessor(workerDb, mediator, publishEndpoint).ProcessAllAsync();
        }

        await using var assertDb = new PersistMessageDbContext<TestModule>(options);
        var rows = await assertDb.PersistMessage.ToListAsync();
        var command = Assert.Single(rows, message => message.DataType == typeof(ConfirmSeat).ToString());
        var released = Assert.Single(rows, message => message.DataType == typeof(SeatReleasedIntegration).ToString());
        Assert.Equal(MessageStatus.InProgress, command.MessageStatus);
        Assert.Equal(MessageStatus.Processed, released.MessageStatus);
    }

    [Fact]
    public async Task ProcessAllAsync_stops_and_rethrows_when_cancelled_mid_batch()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TestModule>>().UseSqlite(connection).Options;
        var mediator = Substitute.For<IMediator>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        using var cancellation = new CancellationTokenSource();
        publishEndpoint
            .When(endpoint =>
                endpoint.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>())
            )
            .Do(_ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });

        await using (var seedDb = new PersistMessageDbContext<TestModule>(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            var processor = CreateProcessor(seedDb, mediator, publishEndpoint);
            await processor.PublishMessageAsync(new MessageEnvelope(new SeatReservedIntegration()));
            await processor.PublishMessageAsync(new MessageEnvelope(new SeatReleasedIntegration()));
        }

        await using (var workerDb = new PersistMessageDbContext<TestModule>(options))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateProcessor(workerDb, mediator, publishEndpoint).ProcessAllAsync(cancellation.Token)
            );
        }

        Assert.Single(publishEndpoint.ReceivedCalls());
        await using var assertDb = new PersistMessageDbContext<TestModule>(options);
        var rows = await assertDb.PersistMessage.ToListAsync();
        Assert.All(rows, message => Assert.NotEqual(MessageStatus.Processed, message.MessageStatus));
    }

    [Fact]
    public async Task ProcessAllAsync_treats_operation_canceled_without_caller_cancellation_as_row_failure()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TestModule>>().UseSqlite(connection).Options;
        var mediator = Substitute.For<IMediator>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        publishEndpoint
            .When(endpoint =>
                endpoint.Publish(Arg.Any<object>(), Arg.Any<IPipe<PublishContext>>(), Arg.Any<CancellationToken>())
            )
            .Do(callInfo =>
            {
                if (callInfo.ArgAt<object>(0) is SeatReservedIntegration)
                    throw new OperationCanceledException();
            });

        await using (var seedDb = new PersistMessageDbContext<TestModule>(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            var processor = CreateProcessor(seedDb, mediator, publishEndpoint);
            await processor.PublishMessageAsync(new MessageEnvelope(new SeatReservedIntegration()));
            await processor.PublishMessageAsync(new MessageEnvelope(new SeatReleasedIntegration()));
        }

        await using (var workerDb = new PersistMessageDbContext<TestModule>(options))
        {
            await CreateProcessor(workerDb, mediator, publishEndpoint).ProcessAllAsync();
        }

        await using var assertDb = new PersistMessageDbContext<TestModule>(options);
        var rows = await assertDb.PersistMessage.ToListAsync();
        var reserved = Assert.Single(rows, message => message.DataType == typeof(SeatReservedIntegration).ToString());
        var released = Assert.Single(rows, message => message.DataType == typeof(SeatReleasedIntegration).ToString());
        Assert.Equal(MessageStatus.InProgress, reserved.MessageStatus);
        Assert.Equal(MessageStatus.Processed, released.MessageStatus);
    }

    [Fact]
    public async Task ProcessAllAsync_throws_before_processing_when_token_already_cancelled()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TestModule>>().UseSqlite(connection).Options;
        var mediator = Substitute.For<IMediator>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();

        await using (var seedDb = new PersistMessageDbContext<TestModule>(options))
        {
            await seedDb.Database.EnsureCreatedAsync();
            await CreateProcessor(seedDb, mediator, publishEndpoint)
                .PublishMessageAsync(new MessageEnvelope(new SeatReservedIntegration()));
        }

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await using (var workerDb = new PersistMessageDbContext<TestModule>(options))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => CreateProcessor(workerDb, mediator, publishEndpoint).ProcessAllAsync(cancellation.Token)
            );
        }

        Assert.Empty(publishEndpoint.ReceivedCalls());
    }

    private static PersistMessageProcessor<TestModule> CreateProcessor(
        PersistMessageDbContext<TestModule> db,
        IMediator mediator,
        IPublishEndpoint publishEndpoint
    ) => new(NullLogger<PersistMessageProcessor<TestModule>>.Instance, mediator, db, publishEndpoint);
}
