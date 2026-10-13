using System.Net;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using Contracts.Grpc.Passenger;
using EasyNetQ.Management.Client;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Passenger.Host.Extensions;
using Passenger.Passengers.Models;
using Xunit;
using PassengerType = Passenger.Passengers.Enums.PassengerType;

namespace Passenger.Host.Test;

[Collection(PassengerHostCollection.Name)]
public class PassengerHostTests : TestBase<Program, PassengerDbContext, PassengerReadDbContext>
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly PassengerHostFixture _fixture;

    public PassengerHostTests(PassengerHostFixture fixture)
        : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task root_should_return_service_name()
    {
        var response = await _fixture.HttpClient.GetStringAsync("/");

        response.Should().Be("Passenger-Service");
    }

    [Fact]
    public async Task liveness_and_readiness_should_be_healthy()
    {
        var live = await _fixture.HttpClient.GetAsync(PassengerServiceHostExtensions.LivenessEndpointPath);
        var ready = await _fixture.HttpClient.GetAsync(PassengerServiceHostExtensions.ReadinessEndpointPath);

        live.StatusCode.Should().Be(HttpStatusCode.OK);
        ready.StatusCode.Should().Be(HttpStatusCode.OK, await ready.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task bus_should_use_rabbitmq_with_passenger_prefixed_consumer_queue()
    {
        var bus = _fixture.ServiceProvider.GetRequiredService<IBus>();
        bus.Address.Scheme.Should().Be("rabbitmq");

        var queues = await _fixture.CreateManagementClient().GetQueuesAsync();
        queues.Select(q => q.Name).Should().Contain(PassengerHostFixture.ConsumerQueue);
    }

    [Fact]
    public async Task user_created_published_to_broker_should_create_passenger_and_inbox_record()
    {
        var passportNumber = $"H{Random.Shared.Next(10_000_000, 99_999_999)}";
        var messageId = NewId.NextGuid();

        var publisher = _fixture.CreateExternalPublisher();
        await publisher.StartAsync();
        try
        {
            await publisher.Publish(
                new UserCreated(NewId.NextGuid(), "Host User", passportNumber),
                ctx => ctx.MessageId = messageId
            );
        }
        finally
        {
            await publisher.StopAsync();
        }

        var passenger = await TestWait.UntilAsync(
            () =>
                _fixture.ExecuteDbContextAsync(db =>
                    db.Passengers.FirstOrDefaultAsync(x => x.PassportNumber.Value == passportNumber)
                ),
            WaitTimeout
        );
        passenger.Should().NotBeNull("Passenger.Host must consume UserCreated from RabbitMQ");
        passenger!.Name.Value.Should().Be("Host User");

        var inboxId = InboxMessageId.Create(
            messageId,
            new Uri($"rabbitmq://localhost/{PassengerHostFixture.ConsumerQueue}")
        );
        var inbox = await TestWait.UntilAsync(
            async () =>
            {
                using var scope = _fixture.ServiceProvider.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor>();
                return (
                    await processor.GetByFilterAsync(x =>
                        x.DeliveryType == MessageDeliveryType.Inbox && x.MessageStatus == MessageStatus.Processed
                    )
                ).FirstOrDefault(x => x.Data.Contains(passportNumber, StringComparison.Ordinal));
            },
            WaitTimeout
        );
        inbox.Should().NotBeNull("the consumer endpoint runs behind the inbox filter");
        inbox!.Id.Should().Be(inboxId);
    }

    [Fact]
    public async Task grpc_get_by_id_should_return_passenger_from_read_model()
    {
        var passengerId = NewId.NextGuid();
        await _fixture.ExecuteReadContextAsync(ctx =>
            ctx.Passenger.InsertOneAsync(
                new PassengerReadModel
                {
                    Id = NewId.NextGuid(),
                    PassengerId = passengerId,
                    Name = "Grpc User",
                    PassportNumber = "G12345678",
                    PassengerType = PassengerType.Female,
                    Age = 31,
                    IsDeleted = false,
                }
            )
        );

        var client = new PassengerGrpcService.PassengerGrpcServiceClient(_fixture.Channel);
        var response = await client.GetByIdAsync(new GetByIdRequest { Id = passengerId.ToString() });

        response.PassengerDto.Id.Should().Be(passengerId.ToString());
        response.PassengerDto.Name.Should().Be("Grpc User");
        response.PassengerDto.PassportNumber.Should().Be("G12345678");
        response.PassengerDto.Age.Should().Be(31);
    }

    [Fact]
    public async Task rest_get_by_id_should_return_passenger_from_read_model()
    {
        var passengerId = NewId.NextGuid();
        await _fixture.ExecuteReadContextAsync(ctx =>
            ctx.Passenger.InsertOneAsync(
                new PassengerReadModel
                {
                    Id = NewId.NextGuid(),
                    PassengerId = passengerId,
                    Name = "Rest User",
                    PassportNumber = "R12345678",
                    PassengerType = PassengerType.Male,
                    Age = 40,
                    IsDeleted = false,
                }
            )
        );

        var response = await _fixture.HttpClient.GetAsync($"/api/v1.0/passenger/{passengerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain("Rest User");
    }
}
