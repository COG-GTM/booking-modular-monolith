using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using Contracts.Grpc.Passenger;
using Grpc.Health.V1;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Passenger;
using Passenger.Data;
using Passenger.Passengers.Enums;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Xunit;

namespace Host.Test;

[Collection(HostTestCollection.Name)]
public class PassengerHostIntegrationTests : PassengerHostTestBase
{
    public PassengerHostIntegrationTests(
        TestFixture<Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> fixture
    )
        : base(fixture) { }

    [Fact]
    public async Task user_created_is_consumed_and_recorded_in_the_inbox()
    {
        var message = new BuildingBlocks.Contracts.EventBus.Messages.UserCreated(Guid.NewGuid(), "Sam", "123456789");
        await Fixture.Publish(message);

        global::Passenger.Passengers.Models.Passenger? passenger = null;
        var receiptExists = false;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while ((passenger is null || !receiptExists) && DateTime.UtcNow < deadline)
        {
            passenger = await Fixture.ExecuteDbContextAsync(db =>
                db.Passengers.AsNoTracking()
                    .SingleOrDefaultAsync(candidate => candidate.PassportNumber.Value == message.PassportNumber)
            );
            using (var scope = Fixture.ServiceProvider.CreateScope())
            {
                receiptExists = await scope.ServiceProvider
                    .GetRequiredService<PersistMessageDbContext<PassengerRoot>>()
                    .InboxMessages.AsNoTracking()
                    .AnyAsync(receipt => receipt.Consumer.Contains("passenger-service-register-new-user-handler"));
            }
            if (passenger is null || !receiptExists)
                await Task.Delay(100);
        }

        Assert.NotNull(passenger);
        Assert.Equal(message.Name, passenger.Name.Value);
        Assert.Equal(message.PassportNumber, passenger.PassportNumber.Value);
        Assert.True(receiptExists);
    }

    [Fact]
    public void only_passenger_consumers_are_registered_for_the_host_queue()
    {
        Assert.Equal("RabbitMq", Fixture.Configuration["Messaging:Transport"]);
        Assert.Equal("rabbitmq", Fixture.ServiceProvider.GetRequiredService<IBusControl>().Address.Scheme);

        var registrations = Fixture.ServiceProvider.GetServices<ConsumerModuleRegistration>().ToArray();

        Assert.NotEmpty(registrations);
        Assert.All(registrations, registration =>
        {
            Assert.Same(typeof(PassengerRoot).Assembly, registration.ConsumerAssembly);
            Assert.StartsWith("passenger-service-", registration.EndpointName, StringComparison.Ordinal);
        });
        Assert.Contains(
            registrations,
            registration => registration.EndpointName == "passenger-service-register-new-user-handler"
        );
    }

    [Fact]
    public async Task grpc_returns_a_passenger_from_the_read_store()
    {
        var id = Guid.NewGuid();
        await Fixture.SendAsync(
            new CompleteRegisterPassengerMongoCommand(
                id,
                "123456789",
                "Sam",
                global::Passenger.Passengers.Enums.PassengerType.Male,
                30
            )
        );

        var client = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.Channel);
        var response = await client.GetByIdAsync(new GetByIdRequest { Id = id.ToString() });

        Assert.Equal(id.ToString(), response.PassengerDto.Id);
    }

    [Fact]
    public async Task rest_returns_a_passenger_from_the_read_store()
    {
        var id = Guid.NewGuid();
        await Fixture.SendAsync(
            new CompleteRegisterPassengerMongoCommand(
                id,
                "123456789",
                "Sam",
                global::Passenger.Passengers.Enums.PassengerType.Male,
                30
            )
        );

        var response = await Fixture.HttpClient.GetAsync($"/api/v1/passenger/{id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(id.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task health_endpoints_report_live_and_ready_dependencies()
    {
        var liveResponse = await Fixture.HttpClient.GetAsync("/health/live");
        var readyResponse = await Fixture.HttpClient.GetAsync("/health/ready");
        var registrations = Fixture.ServiceProvider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations
            .Select(registration => registration.Name)
            .ToArray();

        Assert.Equal(System.Net.HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.Contains("postgres-passenger", registrations);
        Assert.Contains("postgres-passenger-persist-message", registrations);
        Assert.Contains("mongo-passenger", registrations);
        Assert.Contains("rabbitmq", registrations);
    }

    [Fact]
    public async Task grpc_health_reports_passenger_service_as_serving()
    {
        var client = new Health.HealthClient(Fixture.Channel);
        var response = await client.CheckAsync(
            new HealthCheckRequest { Service = "passenger.PassengerGrpcService" }
        );

        Assert.Equal(HealthCheckResponse.Types.ServingStatus.Serving, response.Status);
    }
}

public abstract class PassengerHostTestBase
    : TestBase<Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext>
{
    protected PassengerHostTestBase(
        TestFixture<Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> fixture
    )
        : base(fixture) { }
}

[CollectionDefinition(Name)]
public class HostTestCollection
    : ICollectionFixture<TestFixture<Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "Passenger Host Integration Test";
}
