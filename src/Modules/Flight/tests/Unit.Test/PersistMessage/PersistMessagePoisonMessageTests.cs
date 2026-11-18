namespace Unit.Test.PersistMessage;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

public record PoisonInternalCommand(Guid Id) : InternalCommand;

public record HealthyInternalCommand(Guid Id) : InternalCommand;

public class PersistMessagePoisonMessageTests : IDisposable
{
    private const int MaxRetryCount = 3;

    private readonly PersistMessageDbContext _dbContext;
    private readonly IMediator _mediator;
    private readonly PersistMessageProcessor _processor;

    public PersistMessagePoisonMessageTests()
    {
        var dbOptions = new DbContextOptionsBuilder<PersistMessageDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _dbContext = new PersistMessageDbContext(dbOptions, NullLogger<PersistMessageDbContext>.Instance);
        _mediator = Substitute.For<IMediator>();

        _mediator.Send(Arg.Is<object>(x => x is PoisonInternalCommand), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("poison"));

        _processor = new PersistMessageProcessor(
            NullLogger<PersistMessageProcessor>.Instance,
            _mediator,
            _dbContext,
            Substitute.For<IPublishEndpoint>(),
            Options.Create(new PersistMessageOptions { MaxRetryCount = MaxRetryCount }));
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task process_all_should_not_throw_and_should_process_messages_after_a_failing_one()
    {
        await _processor.AddInternalMessageAsync(new PoisonInternalCommand(Guid.NewGuid()));
        await _processor.AddInternalMessageAsync(new HealthyInternalCommand(Guid.NewGuid()));

        var act = () => _processor.ProcessAllAsync();

        await act.Should().NotThrowAsync();

        var messages = await _dbContext.PersistMessage.AsNoTracking().ToListAsync();
        var poison = messages.Single(x => x.DataType == typeof(PoisonInternalCommand).ToString());
        var healthy = messages.Single(x => x.DataType == typeof(HealthyInternalCommand).ToString());

        healthy.MessageStatus.Should().Be(MessageStatus.Processed);
        poison.MessageStatus.Should().Be(MessageStatus.InProgress);
        poison.RetryCount.Should().Be(1);
    }

    [Fact]
    public async Task process_all_should_mark_message_failed_after_max_retries_and_stop_retrying_it()
    {
        await _processor.AddInternalMessageAsync(new PoisonInternalCommand(Guid.NewGuid()));

        for (var i = 0; i < MaxRetryCount + 2; i++)
        {
            await _processor.ProcessAllAsync();
        }

        var poison = await _dbContext.PersistMessage.AsNoTracking().SingleAsync();

        poison.MessageStatus.Should().Be(MessageStatus.Failed);
        poison.RetryCount.Should().Be(MaxRetryCount);

        await _mediator.Received(MaxRetryCount)
            .Send(Arg.Is<object>(x => x is PoisonInternalCommand), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task background_service_should_keep_running_when_a_processing_cycle_throws()
    {
        var failingProcessor = Substitute.For<IPersistMessageProcessor>();
        failingProcessor.ProcessAllAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("poison"));

        var services = new ServiceCollection();
        services.AddScoped(_ => failingProcessor);
        await using var provider = services.BuildServiceProvider();

        using var service = new PersistMessageBackgroundService(
            NullLogger<PersistMessageBackgroundService>.Instance,
            provider,
            Options.Create(new PersistMessageOptions { Interval = 1 }));

        await service.StartAsync(CancellationToken.None);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (failingProcessor.ReceivedCalls().Count() < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        service.ExecuteTask.Should().NotBeNull();
        service.ExecuteTask!.IsFaulted.Should().BeFalse();
        failingProcessor.ReceivedCalls().Count().Should().BeGreaterThanOrEqualTo(2);

        await service.StopAsync(CancellationToken.None);
    }
}
