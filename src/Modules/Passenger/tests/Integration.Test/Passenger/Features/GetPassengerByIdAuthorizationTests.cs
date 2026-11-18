using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using global::Passenger;
using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.TestBase;
using FluentAssertions;
using global::Passenger.Data;
using global::Passenger.Identity.Consumers.RegisteringNewUser.V1;
using global::Passenger.Passengers.Models;
using Grpc.Core;
using Grpc.Net.Client;
using Integration.Test.Fakes;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using NSubstitute;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class GetPassengerByIdAuthorizationTests : PassengerIntegrationTestBase
{
    public GetPassengerByIdAuthorizationTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }

    private HttpClient CreateClient(Guid? userId, string role = null)
    {
        var claims = new Dictionary<string, object>
        {
            { ClaimTypes.Name, "user@sample.com" },
            { "scope", "flight-api" },
        };

        if (userId.HasValue)
        {
            claims["sub"] = userId.Value.ToString();
        }

        if (role is not null)
        {
            claims[ClaimTypes.Role] = role;
        }

        var client = Fixture.HttpClient;
        client.SetFakeBearerToken(claims);
        return client;
    }

    private static PassengerGrpcService.PassengerGrpcServiceClient GrpcClient(HttpClient httpClient) =>
        new(GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions { HttpClient = httpClient }));

    private async Task<Guid> SeedPassengerAsync(Guid? ownerId)
    {
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = ownerId };
        await Fixture.SendAsync(command);
        return command.Id;
    }

    [Fact]
    public async Task http_get_should_return_passenger_to_its_owner()
    {
        var ownerId = Guid.NewGuid();
        var passengerId = await SeedPassengerAsync(ownerId);

        var response = await CreateClient(ownerId, "user").GetAsync($"api/v1/passenger/{passengerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(passengerId.ToString());
    }

    [Fact]
    public async Task http_get_should_return_not_found_to_another_user()
    {
        var passengerId = await SeedPassengerAsync(Guid.NewGuid());

        var response = await CreateClient(Guid.NewGuid(), "user").GetAsync($"api/v1/passenger/{passengerId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("123456789");
    }

    [Fact]
    public async Task http_get_should_return_not_found_for_passenger_without_owner_to_non_admin()
    {
        var passengerId = await SeedPassengerAsync(null);

        var response = await CreateClient(Guid.NewGuid(), "user").GetAsync($"api/v1/passenger/{passengerId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task http_get_should_return_passenger_of_another_user_to_admin()
    {
        var passengerId = await SeedPassengerAsync(Guid.NewGuid());

        var response = await CreateClient(Guid.NewGuid(), "admin").GetAsync($"api/v1/passenger/{passengerId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task http_get_should_return_same_status_for_missing_and_foreign_passengers()
    {
        var foreignPassengerId = await SeedPassengerAsync(Guid.NewGuid());
        var client = CreateClient(Guid.NewGuid(), "user");

        var foreign = await client.GetAsync($"api/v1/passenger/{foreignPassengerId}");
        var missing = await client.GetAsync($"api/v1/passenger/{Guid.NewGuid()}");

        foreign.StatusCode.Should().Be(missing.StatusCode).And.Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task grpc_get_by_id_should_reject_anonymous_caller()
    {
        var passengerId = await SeedPassengerAsync(Guid.NewGuid());
        var httpClient = Fixture.HttpClient;
        httpClient.DefaultRequestHeaders.Authorization = null;

        var act = async () =>
            await GrpcClient(httpClient).GetByIdAsync(new GetByIdRequest { Id = passengerId.ToString() });

        (await act.Should().ThrowAsync<RpcException>()).Which.StatusCode.Should().Be(StatusCode.Unauthenticated);
    }

    [Fact]
    public async Task grpc_get_by_id_should_return_passenger_to_its_owner()
    {
        var ownerId = Guid.NewGuid();
        var passengerId = await SeedPassengerAsync(ownerId);

        var response = await GrpcClient(CreateClient(ownerId, "user"))
            .GetByIdAsync(new GetByIdRequest { Id = passengerId.ToString() });

        response.PassengerDto.Id.Should().Be(passengerId.ToString());
    }

    [Fact]
    public async Task grpc_get_by_id_should_not_return_passenger_to_another_user()
    {
        var passengerId = await SeedPassengerAsync(Guid.NewGuid());

        var act = async () =>
            await GrpcClient(CreateClient(Guid.NewGuid(), "user"))
                .GetByIdAsync(new GetByIdRequest { Id = passengerId.ToString() });

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.StatusCode.Should().Be(StatusCode.Internal);
        exception.Status.Detail.Should().Be("Passenger not found!");
    }

    [Fact]
    public async Task register_new_user_should_make_created_user_the_passenger_owner()
    {
        var userCreated = new FakeUserCreated().Generate() with { PassportNumber = Guid.NewGuid().ToString("N")[..9] };
        var dispatcher = Substitute.For<IEventDispatcher>();
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(userCreated);

        await ExecuteScope(async sp =>
        {
            var handler = ActivatorUtilities.CreateInstance<RegisterNewUserHandler>(sp, dispatcher);
            await handler.Consume(context);
        });

        var passenger = await Fixture.ExecuteDbContextAsync(db =>
            EntityFrameworkQueryableExtensions.SingleAsync(
                db.Passengers,
                x => x.PassportNumber.Value == userCreated.PassportNumber
            )
        );
        passenger.UserId.Should().Be(userCreated.Id);

        _ = dispatcher
            .Received(1)
            .SendAsync(
                Arg.Is<PassengerCreatedDomainEvent>(e => e.UserId == userCreated.Id && e.Id == passenger.Id.Value),
                typeof(IInternalCommand),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task mongo_projection_should_store_owner_on_insert_and_update()
    {
        var ownerId = Guid.NewGuid();
        var command = new FakeCompleteRegisterPassengerMongoCommand().Generate() with { UserId = null };
        await Fixture.SendAsync(command);

        await Fixture.SendAsync(command with { UserId = ownerId, Age = 41 });

        var readModel = await Fixture.ExecuteReadContextAsync(db =>
            MongoQueryable.SingleAsync(db.Passenger.AsQueryable(), x => x.PassengerId == command.Id)
        );
        readModel.UserId.Should().Be(ownerId);
        readModel.Age.Should().Be(41);
    }

    private async Task ExecuteScope(Func<IServiceProvider, Task> action)
    {
        using var scope = Fixture.ServiceProvider.CreateScope();
        await action(scope.ServiceProvider);
    }
}
