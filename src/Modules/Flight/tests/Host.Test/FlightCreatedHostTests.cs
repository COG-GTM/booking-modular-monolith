using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Data.Seed;
using Flight.Flights.Features.CreatingFlight.V1;
using FluentAssertions;
using Integration.Test;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Host.Test;

[Collection(IntegrationTestCollection.Name)]
public sealed class FlightCreatedHostTests(
    TestFixture<global::Program, FlightDbContext, FlightReadDbContext> fixture
) : FlightIntegrationTestBase(fixture)
{
    [Fact]
    public async Task host_create_flight_persists_and_publishes_flight_created_to_rabbitmq()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        var flightDbContext = scope.ServiceProvider.GetRequiredService<FlightDbContext>();
        var aircraft = await flightDbContext.Aircraft.AsNoTracking().FirstOrDefaultAsync();
        if (aircraft is null)
        {
            await flightDbContext.Aircraft.AddRangeAsync(InitialData.Aircrafts);
            await flightDbContext.SaveChangesAsync();
        }

        var airports = await flightDbContext.Airports.AsNoTracking().ToListAsync();
        if (airports.Count < 2)
        {
            flightDbContext.Airports.AddRange(
                InitialData.Airports.Where(seed => airports.All(existing => existing.Id != seed.Id))
            );
            await flightDbContext.SaveChangesAsync();
            airports = await flightDbContext.Airports.AsNoTracking().ToListAsync();
        }

        var aircraftId = (await flightDbContext.Aircraft.AsNoTracking().FirstAsync()).Id.Value;

        var queueName = $"ab504-flight-created-{Guid.NewGuid():N}";
        using var managementClient = CreateManagementClient();
        await CreateBoundProbeQueue(managementClient, queueName);

        using var response = await Fixture.HttpClient.PostAsJsonAsync(
            "/api/v1.0/flight",
            new CreateFlightRequestDto(
                $"AB{Random.Shared.Next(1000, 9999)}",
                aircraftId,
                airports[0].Id,
                DateTime.UtcNow.AddDays(2),
                DateTime.UtcNow.AddDays(2).AddHours(2),
                airports[1].Id,
                120,
                DateTime.UtcNow.AddDays(2),
                global::Flight.Flights.Enums.FlightStatus.Flying,
                250
            )
        );

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateFlightResponseDto>();
        created.Should().NotBeNull();

        PersistMessage? outboxMessage = null;
        var outboxProcessed = await Fixture.WaitUntilAsync(async () =>
        {
            using var scope = Fixture.ServiceProvider.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<global::Flight.FlightRoot>>();
            outboxMessage = await outbox.PersistMessage.AsNoTracking().SingleOrDefaultAsync(message =>
                message.DataType.Contains(nameof(FlightCreated)) && message.Data.Contains(created!.Id.ToString())
            );
            return outboxMessage?.MessageStatus == MessageStatus.Processed;
        });

        outboxMessage.Should().NotBeNull("the host writes FlightCreated to the service-owned outbox");
        outboxProcessed.Should().BeTrue();

        var deliveredToRabbitMq = await Fixture.WaitUntilAsync(async () =>
        {
            var queue = await GetQueue(managementClient, queueName);
            return queue is { } value && value.TryGetProperty("messages", out var messages) && messages.GetInt32() > 0;
        });
        deliveredToRabbitMq.Should().BeTrue("the real RabbitMQ exchange delivers FlightCreated");
    }

    private HttpClient CreateManagementClient()
    {
        var container = Fixture.RabbitMqTestContainer;
        var options = TestContainers.RabbitMqContainerConfiguration;
        var client = new HttpClient
        {
            BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(options.ApiPort)}/"),
        };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.UserName}:{options.Password}"))
        );
        return client;
    }

    private static async Task CreateBoundProbeQueue(HttpClient client, string queueName)
    {
        var exchange = Uri.EscapeDataString("BuildingBlocks.Contracts.EventBus.Messages:FlightCreated");
        var exchangeResponse = await client.PutAsync(
            $"api/exchanges/%2F/{exchange}",
            new StringContent(
                """{"type":"fanout","durable":true,"auto_delete":false,"internal":false,"arguments":{}}""",
                Encoding.UTF8,
                "application/json"
            )
        );
        exchangeResponse.EnsureSuccessStatusCode();

        var queueResponse = await client.PutAsync(
            $"api/queues/%2F/{Uri.EscapeDataString(queueName)}",
            new StringContent("""{"durable":true,"auto_delete":false,"arguments":{}}""", Encoding.UTF8, "application/json")
        );
        queueResponse.EnsureSuccessStatusCode();

        var bindingResponse = await client.PostAsync(
            $"api/bindings/%2F/e/{exchange}/q/{Uri.EscapeDataString(queueName)}",
            new StringContent("""{"routing_key":"","arguments":{}}""", Encoding.UTF8, "application/json")
        );
        bindingResponse.EnsureSuccessStatusCode();
    }

    private static async Task<System.Text.Json.JsonElement?> GetQueue(HttpClient client, string queueName)
    {
        var response = await client.GetAsync($"api/queues/%2F/{Uri.EscapeDataString(queueName)}");
        if (!response.IsSuccessStatusCode)
            return null;

        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
