using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Identity.Identity.Features.RegisteringNewUser.V1;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using RabbitMQ.Client;
using Xunit;

namespace Identity.Host.Test;

public class IdentityHostTests(IdentityHostTestFixture fixture) : IdentityHostTestBase(fixture)
{
    private const string UserCreatedExchange = "BuildingBlocks.Contracts.EventBus.Messages:UserCreated";
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task host_should_boot_standalone_and_serve_root_endpoint()
    {
        var response = await Fixture.HttpClient.GetStringAsync("/");

        response.Should().Be("Identity-Service");
    }

    [Fact]
    public async Task bus_should_use_rabbitmq_transport()
    {
        var bus = Fixture.ServiceProvider.GetRequiredService<IBus>();

        bus.Address.Scheme.Should().Be("rabbitmq");
    }

    [Fact]
    public async Task discovery_document_should_use_configured_issuer()
    {
        using var document = JsonDocument.Parse(
            await Fixture.HttpClient.GetStringAsync("/.well-known/openid-configuration")
        );

        document.RootElement.GetProperty("issuer").GetString().Should().Be(IdentityHostTestFixture.IssuerUri);
        document.RootElement.GetProperty("token_endpoint").GetString().Should().EndWith("/connect/token");
    }

    [Fact]
    public async Task token_endpoint_should_issue_access_token_for_existing_user()
    {
        var response = await Fixture.HttpClient.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "client",
                    ["client_secret"] = "secret",
                    ["username"] = IdentityHostTestDataSeeder.UserName,
                    ["password"] = IdentityHostTestDataSeeder.Password,
                    ["scope"] = "identity-api flight-api",
                }
            )
        );

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        using var json = JsonDocument.Parse(body);
        var token = new JsonWebToken(json.RootElement.GetProperty("access_token").GetString());

        token.Issuer.Should().Be(IdentityHostTestFixture.IssuerUri);
        token
            .Claims.Where(c => c.Type == "scope")
            .Select(c => c.Value)
            .Should()
            .Contain(["identity-api", "flight-api"]);
        token.Subject.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task token_endpoint_should_reject_invalid_password()
    {
        var response = await Fixture.HttpClient.PostAsync(
            "/connect/token",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "password",
                    ["client_id"] = "client",
                    ["client_secret"] = "secret",
                    ["username"] = IdentityHostTestDataSeeder.UserName,
                    ["password"] = "wrong-password",
                    ["scope"] = "identity-api",
                }
            )
        );

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task register_user_should_persist_user_and_publish_user_created_to_rabbitmq()
    {
        var factory = new ConnectionFactory { Uri = new Uri(Fixture.RabbitMqTestContainer.GetConnectionString()) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        await channel.ExchangeDeclareAsync(UserCreatedExchange, ExchangeType.Fanout, durable: true);
        var probeQueue = (
            await channel.QueueDeclareAsync(string.Empty, durable: false, exclusive: true, autoDelete: true)
        ).QueueName;
        await channel.QueueBindAsync(probeQueue, UserCreatedExchange, string.Empty);

        var passportNumber = $"P{Random.Shared.Next(10_000_000, 99_999_999)}";
        var request = new RegisterNewUserRequestDto(
            "Host",
            "Registered",
            $"reg{Guid.NewGuid():N}"[..20],
            $"{Guid.NewGuid():N}@test.com",
            "Password@123",
            "Password@123",
            passportNumber
        );

        var response = await Fixture.HttpClient.PostAsJsonAsync("/api/v1.0/identity/register-user", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var registered = await response.Content.ReadFromJsonAsync<RegisterNewUserResponseDto>();
        registered!.PassportNumber.Should().Be(passportNumber);

        (await Fixture.ExecuteDbContextAsync(db => db.Users.AnyAsync(u => u.Id == registered.Id)))
            .Should()
            .BeTrue("the user must be stored in the service's own identity database");

        var outboxRow = await WaitForAsync(async () =>
        {
            using var scope = Fixture.ServiceProvider.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor<IdentityRoot>>();
            var rows = await processor.GetByFilterAsync(x => x.DeliveryType == MessageDeliveryType.Outbox);
            return rows.FirstOrDefault(x =>
                x.DataType.Contains(nameof(UserCreated), StringComparison.Ordinal)
                && x.Data.Contains(passportNumber, StringComparison.Ordinal)
            );
        });
        outboxRow.Should().NotBeNull("UserCreated must go through the Identity outbox");

        var delivered = await WaitForAsync(async () =>
        {
            var result = await channel.BasicGetAsync(probeQueue, autoAck: true);
            return result is null ? null : Encoding.UTF8.GetString(result.Body.Span);
        });
        delivered.Should().NotBeNull("UserCreated must be published to the RabbitMQ broker");
        delivered.Should().Contain(passportNumber).And.Contain(registered.Id.ToString());

        Fixture
            .ServiceProvider.GetRequiredService<ITestHarness>()
            .Published.Select<UserCreated>(x => x.Context.MessageId == outboxRow!.Id)
            .Should()
            .ContainSingle("the outbox row id is the broker MessageId");
    }

    private static async Task<T?> WaitForAsync<T>(Func<Task<T?>> probe)
        where T : class
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (await probe() is { } result)
            {
                return result;
            }

            await Task.Delay(250);
        }

        return null;
    }
}
