using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Identity.Identity.Features.RegisteringNewUser.V1;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Host.Test;

public class RegisterNewUserOverHttpTests : IdentityHostTestBase
{
    public RegisterNewUserOverHttpTests(TestWriteFixture<Identity.Host.Program, IdentityContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task registered_user_is_saved_and_published_from_the_host_outbox_to_rabbitmq()
    {
        var passportNumber = $"P{Random.Shared.Next(100_000_000, 999_999_999)}";
        var username = $"host-{Guid.NewGuid():N}";
        var queueName = $"ab493-user-created-{Guid.NewGuid():N}";

        await Fixture
            .ServiceProvider.GetRequiredService<MassTransit.IBus>()
            .Publish(new UserCreated(Guid.NewGuid(), "Exchange Probe", "P000000000"));
        using var managementClient = CreateManagementClient();
        await CreateBoundProbeQueue(managementClient, queueName);

        using var client = Fixture.HttpClient;
        var response = await client.PostAsJsonAsync(
            "/api/v1.0/identity/register-user",
            new RegisterNewUserRequestDto(
                "Standalone",
                "Identity",
                username,
                $"{username}@example.com",
                "Password123",
                "Password123",
                passportNumber
            )
        );

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var registered = await response.Content.ReadFromJsonAsync<RegisterNewUserResponseDto>();
        registered.Should().NotBeNull();
        registered!.Username.Should().Be(username);

        var userExists = await Fixture.ExecuteDbContextAsync(db =>
            db.Users.AsNoTracking().AnyAsync(user => user.UserName == username)
        );
        userExists.Should().BeTrue();

        PersistMessage? outboxMessage = null;
        var processed = await Fixture.WaitUntilAsync(async () =>
        {
            using var scope = Fixture.ServiceProvider.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<
                PersistMessageDbContext<global::Identity.IdentityRoot>
            >();
            outboxMessage = await outbox
                .PersistMessage.AsNoTracking()
                .SingleOrDefaultAsync(message =>
                    message.DataType.Contains(nameof(UserCreated)) && message.Data.Contains(passportNumber)
                );
            return outboxMessage?.MessageStatus == MessageStatus.Processed;
        });

        outboxMessage.Should().NotBeNull("registration writes a UserCreated outbox row");
        processed.Should().BeTrue("the standalone host's outbox worker processes the new row");

        var messageReachedBrokerQueue = await Fixture.WaitUntilAsync(async () =>
        {
            var queue = await GetQueue(managementClient, queueName);
            return queue is { } value && value.TryGetProperty("messages", out var messages) && messages.GetInt32() > 0;
        });
        messageReachedBrokerQueue.Should().BeTrue("the UserCreated exchange delivers to the bound probe queue");
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
        var queueResponse = await client.PutAsync(
            $"api/queues/%2F/{Uri.EscapeDataString(queueName)}",
            new StringContent(
                """{"durable":true,"auto_delete":false,"arguments":{}}""",
                Encoding.UTF8,
                "application/json"
            )
        );
        if (!queueResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"RabbitMQ probe queue creation failed: {await queueResponse.Content.ReadAsStringAsync()}"
            );
        }

        var exchange = Uri.EscapeDataString("BuildingBlocks.Contracts.EventBus.Messages:UserCreated");
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
