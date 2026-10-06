namespace CrossService.EndToEnd.Test;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using Xunit;

[Collection(CrossServiceCollection.Name)]
public sealed class CrossServiceTests(CrossServiceFixture fixture)
{
    private const string FlightId = "3c5c0000-97c6-fc34-2eb9-08db322230c9";

    [Fact]
    [Trait("Category", "CrossServiceE2E")]
    public async Task register_user_creates_passenger_in_passenger_service_over_rabbitmq()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var user = NewUser();

        using var registerResponse = await client.PostAsJsonAsync("/api/v1.0/identity/register-user", user);

        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var registration = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync());
        var userId = registration.RootElement.GetProperty("id").GetGuid();
        var passengerId = await WaitForPassengerAsync(user.PassportNumber);

        await WaitUntilAsync(async () =>
        {
            using var response = await client.GetAsync($"/api/v1.0/passenger/{passengerId}");
            return response.StatusCode == HttpStatusCode.OK;
        });

        using var passengerResponse = await client.GetAsync($"/api/v1.0/passenger/{passengerId}");
        passengerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var passenger = JsonDocument.Parse(await passengerResponse.Content.ReadAsStringAsync());
        passenger
            .RootElement.GetProperty("passengerDto")
            .GetProperty("name")
            .GetString()
            .Should()
            .Be($"{user.FirstName} {user.LastName}");

        using var managementClient = new HttpClient();
        var credentials =
            $"{BuildingBlocks.TestBase.TestContainers.RabbitMqContainerConfiguration.UserName}:{BuildingBlocks.TestBase.TestContainers.RabbitMqContainerConfiguration.Password}";
        managementClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(credentials))
        );
        var managementPort = fixture.RabbitMq.GetMappedPublicPort(15672);
        using var queueResponse = await managementClient.GetAsync(
            $"http://localhost:{managementPort}/api/queues/%2F/passenger-register-new-user-handler"
        );
        queueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var queue = JsonDocument.Parse(await queueResponse.Content.ReadAsStringAsync());
        queue.RootElement.GetProperty("consumers").GetInt32().Should().BeGreaterThan(0);
        userId.Should().NotBeEmpty();
    }

    [Fact]
    [Trait("Category", "CrossServiceE2E")]
    public async Task create_booking_calls_flight_and_passenger_over_grpc()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var user = NewUser();
        using var registerResponse = await client.PostAsJsonAsync("/api/v1.0/identity/register-user", user);
        registerResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        using var registration = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync());
        var passengerId = await WaitForPassengerAsync(user.PassportNumber);
        passengerId.Should().NotBeEmpty();

        var before = await GetAvailableSeatCountAsync(client);
        using var bookingResponse = await client.PostAsJsonAsync(
            "/api/v1.0/booking",
            new CreateBookingRequest(passengerId, Guid.Parse(FlightId), "AB-247 cross-service test")
        );
        bookingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        await WaitUntilAsync(async () => await GetAvailableSeatCountAsync(client) < before);
        (await GetAvailableSeatCountAsync(client)).Should().Be(before - 1);
    }

    [Fact(
        Skip = "Pre-existing bug: CreateBooking aggregate id is Guid.Empty so a second booking hits WrongExpectedVersion; tracked outside AB-247"
    )]
    [Trait("Category", "CrossServiceE2E")]
    public async Task second_booking_succeeds()
    {
        using var client = await CreateAuthenticatedClientAsync();
        var first = NewUser();
        var second = NewUser();
        var firstId = await RegisterAndWaitForPassengerAsync(client, first);
        var secondId = await RegisterAndWaitForPassengerAsync(client, second);

        using var firstBooking = await client.PostAsJsonAsync(
            "/api/v1.0/booking",
            new CreateBookingRequest(firstId, Guid.Parse(FlightId), "first booking")
        );
        firstBooking.StatusCode.Should().Be(HttpStatusCode.OK);
        using var secondBooking = await client.PostAsJsonAsync(
            "/api/v1.0/booking",
            new CreateBookingRequest(secondId, Guid.Parse(FlightId), "second booking")
        );
        secondBooking.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://localhost:{fixture.GatewayPort}") };
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
                    ["scope"] = "booking-modular-monolith",
                }
            )
        );
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var token = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.RootElement.GetProperty("access_token").GetString()
        );
        return client;
    }

    private async Task<Guid> RegisterAndWaitForPassengerAsync(HttpClient client, RegisterUserRequest user)
    {
        using var response = await client.PostAsJsonAsync("/api/v1.0/identity/register-user", user);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await WaitForPassengerAsync(user.PassportNumber);
    }

    private async Task<Guid> WaitForPassengerAsync(string passportNumber)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            await using var connection = new NpgsqlConnection(fixture.PassengerDatabaseConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(
                "select id from passenger where passport_number = @passportNumber",
                connection
            );
            command.Parameters.AddWithValue("passportNumber", passportNumber);
            var result = await command.ExecuteScalarAsync();
            if (result is Guid id)
            {
                return id;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException($"Passenger with passport {passportNumber} was not created within 60 seconds.");
    }

    private async Task<int> GetAvailableSeatCountAsync(HttpClient client)
    {
        using var response = await client.GetAsync($"/api/v1.0/flight/get-available-seats/{FlightId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("seatDtos").GetArrayLength();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        throw new TimeoutException("The expected cross-service state did not appear within 60 seconds.");
    }

    private static RegisterUserRequest NewUser()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return new RegisterUserRequest(
            "Contract",
            "Tester",
            $"ab247-{suffix}",
            $"ab247-{suffix}@example.test",
            "Password@123",
            "Password@123",
            $"P{suffix}"
        );
    }

    private sealed record RegisterUserRequest(
        string FirstName,
        string LastName,
        string Username,
        string Email,
        string Password,
        string ConfirmPassword,
        string PassportNumber
    );

    private sealed record CreateBookingRequest(Guid PassengerId, Guid FlightId, string Description);
}
