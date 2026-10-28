using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Utils;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using PersistMessageProcessorService = BuildingBlocks.PersistMessageProcessor.PersistMessageProcessor;

namespace BuildingBlocks.Infrastructure.Tests;

public class ContractsOutboxResolutionTests
{
    public static IEnumerable<object[]> MovedContractTypes =>
        [
            [typeof(BuildingBlocks.Contracts.EventBus.Messages.UserCreated)],
            [typeof(BuildingBlocks.Contracts.EventBus.Messages.BookingCreated)],
            [typeof(BuildingBlocks.Contracts.EventBus.Messages.FlightCreated)],
            [typeof(BuildingBlocks.Contracts.EventBus.Messages.PassengerCreated)],
            [typeof(BuildingBlocks.Contracts.EventBus.Messages.SeatReserved)],
        ];

    [Theory]
    [MemberData(nameof(MovedContractTypes))]
    public void Outbox_data_type_resolves_to_the_moved_contract(Type type)
    {
        Assert.Same(type, TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(type.ToString()));
    }

    [Fact]
    public async Task Outbox_round_trip_publishes_the_moved_contract()
    {
        var options = new DbContextOptionsBuilder<PersistMessageDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var dbContext = new PersistMessageDbContext(options);
        var mediator = Substitute.For<IMediator>();
        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        var processor = new PersistMessageProcessorService(
            NullLogger<PersistMessageProcessorService>.Instance,
            mediator,
            dbContext,
            publishEndpoint
        );
        var userCreated = new BuildingBlocks.Contracts.EventBus.Messages.UserCreated(Guid.NewGuid(), "name", "P1");

        await processor.PublishMessageAsync(new MessageEnvelope(userCreated));

        var row = Assert.Single(await processor.GetByFilterAsync(x => x.DeliveryType == MessageDeliveryType.Outbox));
        Assert.Equal("BuildingBlocks.Contracts.EventBus.Messages.UserCreated", row.DataType);

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        await publishEndpoint
            .Received(1)
            .Publish(
                Arg.Is<object>(message => message is UserCreated && message.Equals(userCreated)),
                Arg.Any<IPipe<PublishContext>>(),
                Arg.Any<CancellationToken>()
            );
        Assert.Equal(MessageStatus.Processed, row.MessageStatus);
    }
}
