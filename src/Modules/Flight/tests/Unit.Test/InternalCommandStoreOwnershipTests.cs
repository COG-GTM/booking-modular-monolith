using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using Flight.Flights.Features.DeletingFlight.V1;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Unit.Test;

public class InternalCommandStoreOwnershipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task foreign_module_commands_execute_only_when_the_modules_share_a_store(bool sharedStore)
    {
        await using var provider = CreateProvider(sharedStore);
        await using var store = CreateStore();
        var mediator = Substitute.For<IMediator>();
        var logger = Substitute.For<ILogger<PersistMessageProcessor<InternalCommandStoreOwnershipTests>>>();
        var processor = new PersistMessageProcessor<InternalCommandStoreOwnershipTests>(
            logger,
            mediator,
            store,
            Substitute.For<IPublishEndpoint>(),
            provider.GetRequiredService<PersistMessageWorkerStores>()
        );
        var command = new DeleteFlight(Guid.NewGuid());
        await processor.AddInternalMessageAsync(command);

        await processor.ProcessAllAsync();
        await processor.ProcessAllAsync();

        if (sharedStore)
        {
            await mediator
                .Received(1)
                .Send(
                    Arg.Is<object>(value => value is DeleteFlight && ((DeleteFlight)value).Id == command.Id),
                    Arg.Any<CancellationToken>()
                );
            (await store.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
        }
        else
        {
            mediator.ReceivedCalls().Should().BeEmpty();
            (await store.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.InProgress);
            logger
                .ReceivedCalls()
                .Count(call =>
                    call.GetMethodInfo().Name == "Log" && (LogLevel)call.GetArguments()[0]! == LogLevel.Warning
                )
                .Should()
                .Be(2);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task own_module_commands_run_and_mediator_failures_remain_retryable(bool fail)
    {
        await using var provider = CreateProvider(false);
        await using var store = CreateStore();
        var mediator = Substitute.For<IMediator>();
        if (fail)
            mediator
                .Send(Arg.Any<object>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<object?>(new InvalidOperationException("handler failed")));
        var processor = new PersistMessageProcessor<InternalCommandStoreOwnershipTests>(
            NullLogger<PersistMessageProcessor<InternalCommandStoreOwnershipTests>>.Instance,
            mediator,
            store,
            Substitute.For<IPublishEndpoint>(),
            provider.GetRequiredService<PersistMessageWorkerStores>()
        );
        await processor.AddInternalMessageAsync(new LocalCommand(Guid.NewGuid()));

        if (fail)
        {
            var act = () => processor.ProcessAllAsync();
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");
        }
        else
            await processor.ProcessAllAsync();

        (await store.PersistMessage.SingleAsync())
            .MessageStatus.Should()
            .Be(fail ? MessageStatus.InProgress : MessageStatus.Processed);
    }

    private static ServiceProvider CreateProvider(bool sharedStore)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration["ConnectionStrings:flight-persist-message"] =
            "Host=LOCALHOST;Database=flight;Username=flight";
        builder.Configuration["ConnectionStrings:other-persist-message"] =
            $"Server=localhost;Port=5432;Database={(sharedStore ? "flight" : "other")};User Id=other";
        builder.AddPersistMessageProcessor<global::Flight.FlightRoot>("Flight");
        builder.AddPersistMessageProcessor<InternalCommandStoreOwnershipTests>("Other");
        return builder.Services.BuildServiceProvider();
    }

    private static PersistMessageDbContext<InternalCommandStoreOwnershipTests> CreateStore() =>
        new(
            new DbContextOptionsBuilder<PersistMessageDbContext<InternalCommandStoreOwnershipTests>>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    public sealed record LocalCommand(Guid Id) : InternalCommand;
}
