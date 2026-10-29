using Booking.Booking.Features.CreatingBook.V1;
using Booking.Booking.Models;
using Booking.Data;
using Booking.Host;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using EventStore.Client;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using System.Net.Http.Headers;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Host.Test.Integration;

[Collection(BookingHostIntegrationCollection.Name)]
public sealed class BookingHostCrossProcessTests : TestReadBase<Program, BookingReadDbContext>
{
    private readonly BookingHostIntegrationFixture _fixture;

    public BookingHostCrossProcessTests(BookingHostIntegrationFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task persists_and_projects_booking_after_real_flight_and_passenger_grpc_calls()
    {
        var bookingId = Guid.NewGuid();
        var command = new CreateBooking(Guid.NewGuid(), Guid.NewGuid(), "host integration test") { Id = bookingId };
        var queueName = $"ab504-booking-created-{Guid.NewGuid():N}";
        using var managementClient = CreateManagementClient();
        await CreateBoundProbeQueue(managementClient, queueName);

        var result = await Fixture.SendAsync(command);

        result.Should().NotBeNull();
        _fixture.FlightServer.Service.GetByIdCalls.Should().Be(1);
        _fixture.FlightServer.Service.GetAvailableSeatsCalls.Should().Be(1);
        _fixture.FlightServer.Service.ReserveSeatCalls.Should().Be(1);
        _fixture.PassengerServer.Service.GetByIdCalls.Should().Be(1);
        (await Fixture.WaitForPublishing<BookingCreated>()).Should().BeTrue();

        PersistMessage? outboxMessage = null;
        var outboxProcessed = await Fixture.WaitUntilAsync(async () =>
        {
            using var scope = Fixture.ServiceProvider.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<global::Booking.BookingRoot>>();
            outboxMessage = await outbox.PersistMessage.AsNoTracking().SingleOrDefaultAsync(message =>
                message.DataType.Contains(nameof(BookingCreated)) && message.Data.Contains(bookingId.ToString())
            );
            return outboxMessage?.MessageStatus == MessageStatus.Processed;
        });
        outboxMessage.Should().NotBeNull("BookingCreated is written to the service-owned outbox");
        outboxProcessed.Should().BeTrue();

        var deliveredToRabbitMq = await Fixture.WaitUntilAsync(async () =>
        {
            var queue = await GetQueue(managementClient, queueName);
            return queue is { } value && value.TryGetProperty("messages", out var messages) && messages.GetInt32() > 0;
        });
        deliveredToRabbitMq.Should().BeTrue("the real RabbitMQ exchange delivers BookingCreated");

        var eventStoreClient = Fixture.ServiceProvider.GetRequiredService<EventStoreClient>();
        var streamId = StreamNameMapper.ToStreamId<global::Booking.Booking.Models.Booking>(bookingId);
        var events = new List<ResolvedEvent>();
        await foreach (
            var resolvedEvent in eventStoreClient.ReadStreamAsync(
                Direction.Forwards,
                streamId,
                StreamPosition.Start,
                cancellationToken: Fixture.CancellationTokenSource.Token
            )
        )
            events.Add(resolvedEvent);

        events
            .Should()
            .Contain(resolvedEvent =>
                resolvedEvent.Event.EventType.Contains(nameof(BookingCreatedDomainEvent), StringComparison.Ordinal)
            );

        (
            await Fixture.WaitUntilAsync(async () =>
            {
                var projection = await Fixture.ExecuteReadContextAsync(db =>
                    db.Booking.Find(booking => booking.BookId == bookingId).FirstOrDefaultAsync()
                );
                return projection is not null;
            })
        ).Should().BeTrue();

        var projectedBooking = await Fixture.ExecuteReadContextAsync(db =>
            db.Booking.Find(booking => booking.BookId == bookingId).FirstOrDefaultAsync()
        );
        projectedBooking!.BookId.Should().Be(bookingId);
        projectedBooking.PassengerInfo.Name.Should().Be("Host Test Passenger");
        projectedBooking.Trip.SeatNumber.Should().Be("1A");
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
        var exchange = Uri.EscapeDataString("BuildingBlocks.Contracts.EventBus.Messages:BookingCreated");
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
