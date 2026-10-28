namespace CrossService.EndToEnd.Test;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

[Collection(CrossServiceCollection.Name)]
public sealed class CrossServiceFlowTests(CrossServiceFixture fixture)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly Guid SeededFlightId = Guid.Parse("3c5c0000-97c6-fc34-2eb9-08db322230c9");

    [Fact]
    [Trait("Category", "CrossService")]
    public async Task Register_user_through_gateway_creates_passenger_over_rabbitmq()
    {
        var registered = await fixture.RegisterPassengerAsync();
        Guid? passengerId = null;
        await fixture.WaitForAsync(
            async () =>
            {
                passengerId = await fixture.FindPassengerIdAsync(registered.PassportNumber);
                return passengerId.HasValue
                    && await fixture.HasPassengerInboxReceiptAsync()
                    && await fixture.HasProcessedUserCreatedOutboxAsync();
            },
            TimeSpan.FromSeconds(60),
            "passenger creation, inbox receipt, and delivered Identity outbox"
        );

        passengerId.Should().NotBeNull();
    }

    [Fact]
    [Trait("Category", "CrossService")]
    public async Task Create_booking_through_gateway_reserves_seat_over_grpc()
    {
        var registered = await fixture.RegisterPassengerAsync();
        Guid? passengerId = null;
        await fixture.WaitForAsync(
            async () =>
            {
                passengerId = await fixture.FindPassengerIdAsync(registered.PassportNumber);
                return passengerId.HasValue;
            },
            TimeSpan.FromSeconds(60),
            "passenger registration"
        );

        using var client = fixture.CreateGatewayClient(registered.AccessToken);
        using var completed = await client.PostAsJsonAsync(
            "/api/v1/passenger/complete-registration",
            new
            {
                passportNumber = registered.PassportNumber,
                passengerType = 2,
                age = 30,
            },
            WebJson
        );
        var completeBody = await completed.Content.ReadAsStringAsync();
        completed.StatusCode.Should().Be(HttpStatusCode.OK, completeBody);

        await fixture.WaitForAsync(
            async () =>
            {
                using var response = await client.GetAsync($"/api/v1/passenger/{passengerId}");
                return response.StatusCode == HttpStatusCode.OK;
            },
            TimeSpan.FromSeconds(60),
            "Passenger Mongo read model"
        );

        var initialSeatCount = await GetAvailableSeatCountAsync(client);
        using var bookingResponse = await client.PostAsJsonAsync(
            "/api/v1/booking",
            new
            {
                passengerId,
                flightId = SeededFlightId,
                description = "AB-504 cross-service booking",
            },
            WebJson
        );
        var bookingBody = await bookingResponse.Content.ReadAsStringAsync();
        bookingResponse.StatusCode.Should().Be(HttpStatusCode.OK, bookingBody);

        var updatedSeatCount = initialSeatCount;
        await fixture.WaitForAsync(
            async () =>
            {
                updatedSeatCount = await GetAvailableSeatCountAsync(client);
                return updatedSeatCount == initialSeatCount - 1;
            },
            TimeSpan.FromSeconds(60),
            "Flight gRPC reservation to reduce available seats by one"
        );

        var collection = fixture
            .MongoClient.GetDatabase(fixture.BookingDatabaseName)
            .GetCollection<BsonDocument>("booking");
        long bookingCount = 0;
        await fixture.WaitForAsync(
            async () =>
            {
                bookingCount = await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
                return bookingCount > 0;
            },
            TimeSpan.FromSeconds(60),
            "Booking EventStoreDB projection in Mongo"
        );
        updatedSeatCount.Should().Be(initialSeatCount - 1);
        bookingCount.Should().BeGreaterThan(0);
    }

    [Fact]
    [Trait("Category", "CrossService")]
    public async Task Gateway_rejects_unauthenticated_booking()
    {
        using var client = fixture.CreateGatewayClient();
        using var response = await client.PostAsJsonAsync(
            "/api/v1/booking",
            new
            {
                passengerId = Guid.NewGuid(),
                flightId = SeededFlightId,
                description = "unauthenticated",
            },
            WebJson
        );

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<int> GetAvailableSeatCountAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"/api/v1/flight/get-available-seats/{SeededFlightId}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("seatDtos").GetArrayLength();
    }
}
