using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NSubstitute;
using Testcontainers.PostgreSql;
using Xunit;

namespace Messaging.Integration.Test;

public class InboxDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:16").Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    public PersistMessageDbContext CreateContext(string? connectionString = null) =>
        new(
            new DbContextOptionsBuilder<PersistMessageDbContext>()
                .UseNpgsql(connectionString ?? ConnectionString)
                .UseSnakeCaseNamingConvention()
                .Options
        );

    public static PersistMessageProcessor CreateProcessor(PersistMessageDbContext db) =>
        new(
            NullLogger<PersistMessageProcessor>.Instance,
            Substitute.For<IMediator>(),
            db,
            Substitute.For<IPublishEndpoint>()
        );
}

public class InboxExecutionTests(InboxDatabaseFixture fixture) : IClassFixture<InboxDatabaseFixture>
{
    [Fact]
    public async Task Recorded_receipt_skips_the_handler_for_the_same_consumer()
    {
        var messageId = Guid.NewGuid();
        var runs = 0;
        var before = DateTime.UtcNow.AddSeconds(-5);
        await using var db = fixture.CreateContext();
        var processor = InboxDatabaseFixture.CreateProcessor(db);

        await processor.ExecuteInboxAsync(messageId, "/passenger-handler", Count);
        await processor.ExecuteInboxAsync(messageId, "/passenger-handler", Count);

        Assert.Equal(1, runs);
        await using var verify = fixture.CreateContext();
        var receipt = await verify.InboxMessages.SingleAsync(x => x.MessageId == messageId);
        Assert.Equal("/passenger-handler", receipt.Consumer);
        Assert.InRange(receipt.ProcessedAt, before, DateTime.UtcNow.AddSeconds(5));

        Task Count()
        {
            runs++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Each_consumer_keeps_an_independent_receipt_for_the_same_message()
    {
        var messageId = Guid.NewGuid();
        var consumers = new List<string>();
        await using var db = fixture.CreateContext();
        var processor = InboxDatabaseFixture.CreateProcessor(db);

        foreach (var consumer in new[] { "/passenger-handler", "/booking-handler", "/passenger-handler" })
            await processor.ExecuteInboxAsync(
                messageId,
                consumer,
                () =>
                {
                    consumers.Add(consumer);
                    return Task.CompletedTask;
                }
            );

        Assert.Equal(["/passenger-handler", "/booking-handler"], consumers);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, await verify.InboxMessages.CountAsync(x => x.MessageId == messageId));
    }

    [Fact]
    public async Task Failed_handler_rolls_back_its_writes_and_leaves_no_receipt_so_redelivery_runs_again()
    {
        var messageId = Guid.NewGuid();
        var sideEffectId = Guid.NewGuid();
        await using var db = fixture.CreateContext();
        var processor = InboxDatabaseFixture.CreateProcessor(db);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () =>
                processor.ExecuteInboxAsync(
                    messageId,
                    "/passenger-handler",
                    async () =>
                    {
                        db.PersistMessage.Add(
                            new PersistMessage(sideEffectId, "probe", "{}", MessageDeliveryType.Internal)
                        );
                        await db.SaveChangesAsync();
                        throw new InvalidOperationException("Handler failed");
                    }
                )
        );

        Assert.Empty(db.ChangeTracker.Entries());
        await using (var verify = fixture.CreateContext())
        {
            Assert.False(await verify.InboxMessages.AnyAsync(x => x.MessageId == messageId));
            Assert.False(await verify.PersistMessage.AnyAsync(x => x.Id == sideEffectId));
        }

        var redelivered = false;
        await processor.ExecuteInboxAsync(
            messageId,
            "/passenger-handler",
            () =>
            {
                redelivered = true;
                return Task.CompletedTask;
            }
        );

        Assert.True(redelivered);
        await using var after = fixture.CreateContext();
        Assert.True(await after.InboxMessages.AnyAsync(x => x.MessageId == messageId));
    }

    [Fact]
    public async Task Concurrent_redelivery_waits_for_the_in_flight_handler_and_then_skips()
    {
        var messageId = Guid.NewGuid();
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRan = false;
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();

        var first = InboxDatabaseFixture
            .CreateProcessor(firstDb)
            .ExecuteInboxAsync(
                messageId,
                "/passenger-handler",
                async () =>
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task;
                }
            );
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = InboxDatabaseFixture
            .CreateProcessor(secondDb)
            .ExecuteInboxAsync(
                messageId,
                "/passenger-handler",
                () =>
                {
                    secondRan = true;
                    return Task.CompletedTask;
                }
            );

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.False(second.IsCompleted);
        releaseFirst.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(secondRan);
        await using var verify = fixture.CreateContext();
        Assert.Equal(1, await verify.InboxMessages.CountAsync(x => x.MessageId == messageId));
    }

    [Fact]
    public async Task Different_consumers_do_not_block_each_other()
    {
        var messageId = Guid.NewGuid();
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();

        var first = InboxDatabaseFixture
            .CreateProcessor(firstDb)
            .ExecuteInboxAsync(
                messageId,
                "/passenger-handler",
                async () =>
                {
                    firstStarted.SetResult();
                    await releaseFirst.Task;
                }
            );
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await InboxDatabaseFixture
            .CreateProcessor(secondDb)
            .ExecuteInboxAsync(messageId, "/booking-handler", () => Task.CompletedTask)
            .WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(first.IsCompleted);
        releaseFirst.SetResult();
        await first;
    }

    [Fact]
    public async Task Startup_table_bootstrap_creates_an_idempotent_inbox_table_keyed_by_message_and_consumer()
    {
        var database = "inbox_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(fixture.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"create database {database}", admin);
            await create.ExecuteNonQueryAsync();
        }
        var connectionString = new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
        {
            Database = database,
        }.ConnectionString;
        await using var db = fixture.CreateContext(connectionString);

        db.CreatePersistMessageTableIfNotExists();
        db.CreatePersistMessageTableIfNotExists();

        var messageId = Guid.NewGuid();
        db.InboxMessages.AddRange(
            new InboxMessage
            {
                MessageId = messageId,
                Consumer = "/passenger-handler",
                ProcessedAt = DateTime.UtcNow,
            },
            new InboxMessage
            {
                MessageId = messageId,
                Consumer = "/booking-handler",
                ProcessedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        await using var duplicate = new NpgsqlConnection(connectionString);
        await duplicate.OpenAsync();
        await using var insert = new NpgsqlCommand(
            "insert into inbox_message (message_id, consumer, processed_at) values (@id, '/passenger-handler', now())",
            duplicate
        );
        insert.Parameters.AddWithValue("id", messageId);
        var error = await Assert.ThrowsAsync<PostgresException>(() => insert.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
    }
}
