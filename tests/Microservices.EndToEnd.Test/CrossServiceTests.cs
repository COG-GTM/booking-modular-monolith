namespace Microservices.EndToEnd.Test;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EventStore.Client;
using FluentAssertions;
using Npgsql;
using Xunit;

[Collection(MicroservicesCollection.Name)]
[Trait("Category", "CrossServiceE2E")]
public sealed class CrossServiceTests(MicroservicesFixture fixture)
{
    // Seeded by Flight's FlightDataSeeder (flight BD467 with seats 12A..12C, ...).
    private const string FlightId = "3c5c0000-97c6-fc34-2eb9-08db322230c9";
    private const string FlightNumber = "BD467";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task register_user_via_identity_creates_passenger_over_rabbitmq()
    {
        using var client = await GatewayClientAsync();
        var user = NewUser();

        using var register = await client.PostAsJsonAsync("/api/v1.0/identity/register-user", user);
        (await register.Content.ReadAsStringAsync()).Should().Contain(user.Username, $"status {register.StatusCode}");
        register.StatusCode.Should().Be(HttpStatusCode.OK);

        var passengerId = await WaitForPassengerAsync(user.PassportNumber);
        var passengerJson = await WaitForPassengerReadModelAsync(client, passengerId);
        using var body = JsonDocument.Parse(passengerJson);
        body.RootElement.GetProperty("passengerDto")
            .GetProperty("passportNumber")
            .GetString()
            .Should()
            .Be(user.PassportNumber);

        // The Passenger service consumed UserCreated from its own RabbitMQ queue (not in-memory).
        var queues = await RabbitQueuesAsync();
        queues
            .Should()
            .Contain(q =>
                q.Name.StartsWith("passenger", StringComparison.Ordinal)
                && q.Name.Contains("register-new-user", StringComparison.Ordinal)
                && q.Consumers > 0
            );
    }

    [Fact]
    public async Task create_booking_via_gateway_calls_flight_and_passenger_over_grpc_and_persists_to_eventstore()
    {
        using var client = await GatewayClientAsync();
        var user = NewUser();
        using (var register = await client.PostAsJsonAsync("/api/v1.0/identity/register-user", user))
        {
            register.StatusCode.Should().Be(HttpStatusCode.OK, await register.Content.ReadAsStringAsync());
        }

        var passengerId = await WaitForPassengerAsync(user.PassportNumber);
        // Booking resolves the passenger over gRPC from the same read model.
        await WaitForPassengerReadModelAsync(client, passengerId);
        var seatsBefore = await AvailableSeatsAsync(client);
        seatsBefore.Should().BeGreaterThan(0);

        var description = $"ab-504 e2e {Guid.NewGuid():N}";
        using var booking = await client.PostAsJsonAsync(
            "/api/v1.0/booking",
            new
            {
                PassengerId = passengerId,
                FlightId = Guid.Parse(FlightId),
                Description = description,
            }
        );
        var bookingBody = await booking.Content.ReadAsStringAsync();
        booking.StatusCode.Should().Be(HttpStatusCode.OK, $"{bookingBody}\n{fixture.Logs}");

        // Flight.ReserveSeat over gRPC removed one seat from the Flight service's own database.
        await WaitUntilAsync(async () => await AvailableSeatsAsync(client) == seatsBefore - 1, "seat reserved");

        // The booking event in Booking's EventStoreDB carries data only Flight (GetById) and Passenger
        // (GetById) can supply, so both gRPC calls crossed the process boundary.
        var recorded = await WaitForBookingEventAsync(description);
        recorded.Should().Contain(FlightNumber).And.Contain($"{user.FirstName} {user.LastName}");
    }

    private async Task<HttpClient> GatewayClientAsync()
    {
        var client = new HttpClient { BaseAddress = fixture.GatewayAddress, Timeout = TimeSpan.FromSeconds(60) };
        using var response = await client.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "client",
                    ["client_secret"] = "secret",
                    ["username"] = "samh",
                    ["password"] = "Admin@123456",
                    ["scope"] = "booking-modular-monolith identity-api flight-api passenger-api booking-api",
                }
            )
        );
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using var token = JsonDocument.Parse(body);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.RootElement.GetProperty("access_token").GetString()
        );
        return client;
    }

    private async Task<Guid> WaitForPassengerAsync(string passportNumber)
    {
        Guid id = default;
        await WaitUntilAsync(
            async () =>
            {
                await using var connection = new NpgsqlConnection(fixture.ConnectionString("passenger_service"));
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand(
                    "select id from passenger where passport_number = @passport",
                    connection
                );
                command.Parameters.AddWithValue("passport", passportNumber);
                if (await command.ExecuteScalarAsync() is Guid found)
                {
                    id = found;
                    return true;
                }

                return false;
            },
            $"passenger {passportNumber} in passenger_service"
        );
        return id;
    }

    private static async Task<int> AvailableSeatsAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"/api/v1.0/flight/get-available-seats/{FlightId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("seatDtos").GetArrayLength();
    }

    // GET reads Passenger's Mongo read model, which is projected asynchronously after the write.
    private async Task<string> WaitForPassengerReadModelAsync(HttpClient client, Guid passengerId)
    {
        var json = string.Empty;
        await WaitUntilAsync(
            async () =>
            {
                using var passenger = await client.GetAsync($"/api/v1.0/passenger/{passengerId}");
                json = await passenger.Content.ReadAsStringAsync();
                return passenger.StatusCode == HttpStatusCode.OK;
            },
            $"passenger {passengerId} read model via gateway"
        );
        return json;
    }

    private async Task<string> WaitForBookingEventAsync(string description)
    {
        using var eventStore = new EventStoreClient(
            EventStoreClientSettings.Create(fixture.EventStoreConnectionString)
        );
        string? found = null;
        await WaitUntilAsync(
            async () =>
            {
                await foreach (var resolved in eventStore.ReadAllAsync(Direction.Forwards, Position.Start))
                {
                    if (!resolved.Event.EventType.Contains("BookingCreated", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var data = Encoding.UTF8.GetString(resolved.Event.Data.Span);
                    if (data.Contains(description, StringComparison.Ordinal))
                    {
                        found = data;
                        return true;
                    }
                }

                return false;
            },
            "BookingCreated event in EventStoreDB"
        );
        return found!;
    }

    private async Task<IReadOnlyList<(string Name, int Consumers)>> RabbitQueuesAsync()
    {
        using var client = new HttpClient { BaseAddress = fixture.RabbitManagementAddress };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{MicroservicesFixture.RabbitUser}:{MicroservicesFixture.RabbitPassword}")
            )
        );
        using var queues = JsonDocument.Parse(await client.GetStringAsync("/api/queues"));
        return queues
            .RootElement.EnumerateArray()
            .Select(q =>
                (q.GetProperty("name").GetString()!, q.TryGetProperty("consumers", out var c) ? c.GetInt32() : 0)
            )
            .ToList();
    }

    private async Task WaitUntilAsync(Func<Task<bool>> condition, string what)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"timed out waiting for {what}\n{fixture.Logs}");
    }

    private static RegisterUser NewUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new RegisterUser(
            "E2e",
            $"User{suffix}",
            $"e2e-{suffix}",
            $"e2e-{suffix}@example.test",
            "Password@123",
            "Password@123",
            $"P{suffix}"
        );
    }

    private sealed record RegisterUser(
        string FirstName,
        string LastName,
        string Username,
        string Email,
        string Password,
        string ConfirmPassword,
        string PassportNumber
    );
}
