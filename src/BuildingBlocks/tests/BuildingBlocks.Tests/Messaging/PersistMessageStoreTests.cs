using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Testcontainers.PostgreSql;
using Xunit;

namespace BuildingBlocks.Tests.Messaging;

public sealed class PersistMessagePostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } =
        new PostgreSqlBuilder().WithImage("postgres:16").WithDatabase("persist_message").Build();

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        await Container.StartAsync();
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync() => Container.DisposeAsync().AsTask();

    public PersistMessageDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<PersistMessageDbContext>()
                .UseNpgsql(Container.GetConnectionString())
                .UseSnakeCaseNamingConvention()
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options
        );
}

// Inbox/outbox store behaviour against a real Postgres persist_message table.
public class PersistMessageStoreTests(PersistMessagePostgresFixture fixture)
    : IClassFixture<PersistMessagePostgresFixture>
{
    [Fact]
    public async Task second_insert_of_the_same_inbox_id_reports_a_concurrent_duplicate_instead_of_failing()
    {
        var inboxId = Guid.NewGuid();
        await using var first = fixture.CreateContext();
        await using var second = fixture.CreateContext();

        (await Processor(first).TryAddReceivedMessageAsync(Envelope(), inboxId)).Should().BeTrue();
        (await Processor(second).TryAddReceivedMessageAsync(Envelope(), inboxId)).Should().BeFalse();

        second.PersistMessage.Local.Should().BeEmpty();
        await using var check = fixture.CreateContext();
        (await check.PersistMessage.CountAsync(x => x.Id == inboxId)).Should().Be(1);
    }

    [Fact]
    public async Task background_poll_does_not_pick_up_unprocessed_inbox_rows()
    {
        var inboxId = Guid.NewGuid();
        await using var db = fixture.CreateContext();
        var publish = Substitute.For<IPublishEndpoint>();
        var mediator = Substitute.For<IMediator>();
        var processor = new PersistMessageProcessor.PersistMessageProcessor(
            NullLogger<PersistMessageProcessor.PersistMessageProcessor>.Instance,
            mediator,
            db,
            publish
        );

        await processor.TryAddReceivedMessageAsync(Envelope(), inboxId);
        await processor.ProcessAllAsync();

        var row = await db.PersistMessage.AsNoTracking().SingleAsync(x => x.Id == inboxId);
        row.MessageStatus.Should().Be(MessageStatus.InProgress);
        row.RetryCount.Should().Be(0);
        mediator.ReceivedCalls().Should().BeEmpty();
        publish.ReceivedCalls().Should().BeEmpty();
    }

    private static PersistMessageProcessor.PersistMessageProcessor Processor(PersistMessageDbContext db) =>
        new(
            NullLogger<PersistMessageProcessor.PersistMessageProcessor>.Instance,
            Substitute.For<IMediator>(),
            db,
            Substitute.For<IPublishEndpoint>()
        );

    private static MessageEnvelope Envelope() => new(new StoreTestEvent("x"), new Dictionary<string, object?>());

    public sealed record StoreTestEvent(string Value);
}

public class PersistMessageBackgroundServiceTests
{
    [Fact]
    public async Task a_failed_poll_is_logged_and_the_worker_keeps_polling()
    {
        var processor = Substitute.For<IPersistMessageProcessor>();
        var calls = 0;
        var secondPoll = new TaskCompletionSource();
        processor
            .ProcessAllAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                    throw new InvalidOperationException("broker unreachable");
                secondPoll.TrySetResult();
                return Task.CompletedTask;
            });

        var services = new ServiceCollection().AddScoped(_ => processor).BuildServiceProvider();
        var worker = new PersistMessageBackgroundService(
            NullLogger<PersistMessageBackgroundService>.Instance,
            services,
            Options.Create(new PersistMessageOptions { Interval = 0 })
        );

        await worker.StartAsync(CancellationToken.None);
        var completed = await Task.WhenAny(secondPoll.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        await worker.StopAsync(CancellationToken.None);

        completed.Should().Be(secondPoll.Task);
        worker.ExecuteTask!.IsFaulted.Should().BeFalse();
    }
}
