namespace Unit.Test.PersistMessageProcessing;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

public class PersistMessageBatchTests : IDisposable
{
    private readonly PersistMessageDbContext _dbContext;
    private readonly IMediator _mediator = Substitute.For<IMediator>();

    public PersistMessageBatchTests()
    {
        _dbContext = new PersistMessageDbContext(
            new DbContextOptionsBuilder<PersistMessageDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            NullLogger<PersistMessageDbContext>.Instance
        );
    }

    public void Dispose() => _dbContext.Dispose();

    private PersistMessageProcessor CreateProcessor(PersistMessageOptions options) =>
        new(
            NullLogger<PersistMessageProcessor>.Instance,
            _mediator,
            _dbContext,
            Substitute.For<IPublishEndpoint>(),
            Options.Create(options)
        );

    [Fact]
    public async Task process_all_should_limit_each_batch_and_process_oldest_first()
    {
        var processor = CreateProcessor(new PersistMessageOptions { BatchSize = 2 });
        var ids = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var created = DateTime.UtcNow;
        for (var i = 0; i < ids.Length; i++)
        {
            await processor.AddInternalMessageAsync(new HealthyInternalCommand(ids[i]));
            var message = _dbContext.PersistMessage.Local.Single(x =>
                x.Data.Contains(ids[i].ToString(), StringComparison.Ordinal)
            );
            _dbContext.Entry(message).Property(x => x.Created).CurrentValue = created.AddMinutes(-i);
        }
        await _dbContext.SaveChangesAsync();
        var processed = new List<Guid>();
        _mediator
            .Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                processed.Add(((HealthyInternalCommand)call[0]).Id);
                return Task.FromResult<object>(Unit.Value);
            });

        await processor.ProcessAllAsync();

        processed.Should().Equal(ids[2], ids[1]);
        var messages = await _dbContext.PersistMessage.AsNoTracking().ToListAsync();
        messages.Count(x => x.MessageStatus == MessageStatus.Processed).Should().Be(2);
        messages.Count(x => x.MessageStatus == MessageStatus.InProgress).Should().Be(1);

        await processor.ProcessAllAsync();

        processed.Should().Equal(ids[2], ids[1], ids[0]);
    }

    [Fact]
    public async Task process_all_should_skip_failed_messages_without_using_batch_capacity()
    {
        var processor = CreateProcessor(new PersistMessageOptions { BatchSize = 1 });
        await processor.AddInternalMessageAsync(new PoisonInternalCommand(Guid.NewGuid()));
        var failed = await _dbContext.PersistMessage.SingleAsync();
        failed.ChangeState(MessageStatus.Failed);
        _dbContext.Entry(failed).Property(x => x.Created).CurrentValue = DateTime.UtcNow.AddDays(-1);
        await _dbContext.SaveChangesAsync();
        await processor.AddInternalMessageAsync(new HealthyInternalCommand(Guid.NewGuid()));

        await processor.ProcessAllAsync();

        await _mediator
            .DidNotReceive()
            .Send(Arg.Is<object>(x => x is PoisonInternalCommand), Arg.Any<CancellationToken>());
        await _mediator
            .Received(1)
            .Send(Arg.Is<object>(x => x is HealthyInternalCommand), Arg.Any<CancellationToken>());
        var persisted = await _dbContext.PersistMessage.AsNoTracking().SingleAsync(x => x.Id == failed.Id);
        persisted.MessageStatus.Should().Be(MessageStatus.Failed);
        persisted.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task process_all_should_honor_custom_max_retry_count()
    {
        var processor = CreateProcessor(new PersistMessageOptions { MaxRetryCount = 2 });
        _mediator
            .Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("poison"));
        await processor.AddInternalMessageAsync(new PoisonInternalCommand(Guid.NewGuid()));

        await processor.ProcessAllAsync();
        var firstAttempt = await _dbContext.PersistMessage.AsNoTracking().SingleAsync();
        firstAttempt.RetryCount.Should().Be(1);
        firstAttempt.MessageStatus.Should().Be(MessageStatus.InProgress);

        await processor.ProcessAllAsync();
        await processor.ProcessAllAsync();

        var finalAttempt = await _dbContext.PersistMessage.AsNoTracking().SingleAsync();
        finalAttempt.RetryCount.Should().Be(2);
        finalAttempt.MessageStatus.Should().Be(MessageStatus.Failed);
        await _mediator.Received(2).Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task process_all_should_not_increment_retry_count_for_successful_messages()
    {
        var processor = CreateProcessor(new PersistMessageOptions());
        await processor.AddInternalMessageAsync(new HealthyInternalCommand(Guid.NewGuid()));

        await processor.ProcessAllAsync();

        var message = await _dbContext.PersistMessage.AsNoTracking().SingleAsync();
        message.MessageStatus.Should().Be(MessageStatus.Processed);
        message.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task process_all_should_do_nothing_when_there_are_no_messages()
    {
        await CreateProcessor(new PersistMessageOptions()).ProcessAllAsync();

        await _mediator.DidNotReceive().Send(Arg.Any<object>(), Arg.Any<CancellationToken>());
        (await _dbContext.PersistMessage.CountAsync()).Should().Be(0);
    }
}
