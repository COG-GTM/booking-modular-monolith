using System.Net;
using System.Net.Http.Json;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Grpc.Health.V1;
using Grpc.Net.Client;
using MassTransit;
using MassTransit.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passenger;
using Passenger.Data;
using Passenger.Host.Test.Fakes;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Passenger.Passengers.Models;
using Xunit;

namespace Passenger.Host.Test;

public sealed class PassengerHostTests(
    TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> fixture
) : PassengerHostTestBase(fixture)
{
    [Fact]
    public async Task user_created_is_consumed_and_persisted_to_the_passenger_inbox()
    {
        var messageId = Guid.NewGuid();
        var bus = Fixture.ServiceProvider.GetRequiredService<IBus>();

        await bus.Publish(
            new UserCreated(Guid.NewGuid(), "Passenger User", "7123456789"),
            context => context.MessageId = messageId
        );

        var inboxId = InboxMessageId.For("passenger.register-new-user-handler", messageId);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (true)
        {
            using var scope = Fixture.ServiceProvider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<
                PersistMessageDbContext<global::Passenger.PassengerRoot>
            >();
            var processed = await store.PersistMessage.AnyAsync(
                message => message.Id == inboxId && message.MessageStatus == MessageStatus.Processed,
                timeout.Token
            );
            if (processed)
                break;

            await Task.Delay(50, timeout.Token);
        }

        using var verifyScope = Fixture.ServiceProvider.CreateScope();
        var passengers = verifyScope.ServiceProvider.GetRequiredService<PassengerDbContext>();
        (await passengers.Passengers.AnyAsync(passenger => passenger.PassportNumber.Value == "7123456789"))
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task grpc_get_by_id_returns_the_passenger_read_model()
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate();
        await Fixture.SendAsync(command);

        var client = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.Channel);
        var response = await client.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        response.PassengerDto.Id.Should().Be(command.Id.ToString());
        response.PassengerDto.Name.Should().Be(command.Name);
    }

    [Fact]
    public async Task rest_get_by_id_returns_the_passenger_read_model()
    {
        var id = Guid.NewGuid();
        await Fixture.InsertMongoDbContextAsync(
            "passenger",
            new PassengerReadModel
            {
                Id = id,
                PassengerId = id,
                PassportNumber = "123456789",
                Name = "REST Passenger",
                PassengerType = global::Passenger.Passengers.Enums.PassengerType.Male,
                Age = 30,
                IsDeleted = false,
            }
        );

        var response = await Fixture.HttpClient.GetAsync($"/api/v1/passenger/{id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<GetPassengerByIdResponseDto>();
        payload.Should().NotBeNull();
        payload!.PassengerDto.Id.Should().Be(id);
        payload.PassengerDto.Name.Should().Be("REST Passenger");
    }

    [Fact]
    public async Task readiness_liveness_and_grpc_health_are_serving()
    {
        (await Fixture.HttpClient.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Fixture.HttpClient.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);

        var healthClient = new Grpc.Health.V1.Health.HealthClient(Fixture.Channel);
        var response = await healthClient.CheckAsync(
            new HealthCheckRequest { Service = PassengerGrpcService.Descriptor.FullName }
        );

        response.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
        var overallStatus = await healthClient.CheckAsync(new HealthCheckRequest());
        overallStatus.Status.Should().Be(HealthCheckResponse.Types.ServingStatus.Serving);
    }

    [Fact]
    public void composition_registers_only_passenger_workers_and_dependencies()
    {
        Fixture
            .PersistWorkerTypes.Should()
            .ContainSingle()
            .Which.Should()
            .Be(typeof(PersistMessageBackgroundService<global::Passenger.PassengerRoot>));
        Fixture
            .ServiceProvider.GetRequiredService<IPersistMessageProcessor<global::Passenger.PassengerRoot>>()
            .Should()
            .NotBeNull();

        var consumerRegistrations = Fixture.ServiceProvider.GetServices<IConsumerRegistration>().ToArray();
        consumerRegistrations.Should().ContainSingle();

        var referencedAssemblies = typeof(global::Passenger.Host.Program)
            .Assembly.GetReferencedAssemblies()
            .Select(assembly => assembly.Name);
        referencedAssemblies.Should().NotContain(["Api", "Flight", "Identity", "Booking", "BuildingBlocks"]);
    }
}
