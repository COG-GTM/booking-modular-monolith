using System.Diagnostics;
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

public class PersistMessageProcessorTracingTests
{
    [Fact]
    public async Task Processor_restores_stored_request_context_when_publishing()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<PersistMessageDbContext<TestModule>>().UseSqlite(connection).Options;
        var message = new SeatReservedIntegration();
        ActivityTraceId traceId;
        ActivitySpanId parentSpanId;
        await using (var db = new PersistMessageDbContext<TestModule>(options))
        {
            await db.Database.EnsureCreatedAsync();
            using var request = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
            traceId = request.TraceId;
            parentSpanId = request.SpanId;
            await CreateProcessor(db).PublishMessageAsync(new MessageEnvelope(message));
        }

        Activity? published = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == PersistMessageTracing.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => published = activity,
        };
        ActivitySource.AddActivityListener(listener);
        await using var workerDb = new PersistMessageDbContext<TestModule>(options);
        var stored = await workerDb.PersistMessage.SingleAsync();
        Assert.Contains("traceparent", stored.Data, StringComparison.Ordinal);
        await CreateProcessor(workerDb).ProcessAsync(stored.Id, MessageDeliveryType.Outbox);
        Assert.NotNull(published);
        Assert.Equal(traceId, published.TraceId);
        Assert.Equal(parentSpanId, published.ParentSpanId);
        Assert.Equal(MessageStatus.Processed, stored.MessageStatus);
    }

    private static PersistMessageProcessor<TestModule> CreateProcessor(PersistMessageDbContext<TestModule> db) =>
        new(
            NullLogger<PersistMessageProcessor<TestModule>>.Instance,
            Substitute.For<IMediator>(),
            db,
            Substitute.For<IPublishEndpoint>()
        );
}
