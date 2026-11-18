using System.Net;
using System.Net.Http.Json;
using Api;
using BuildingBlocks.Constants;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.TestBase;
using BuildingBlocks.Web;
using FluentAssertions;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Passenger;
using Passenger.Data;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class RegisterNewUserOwnershipTests : PassengerIntegrationTestBase
{
    public RegisterNewUserOwnershipTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task consumer_should_preserve_user_id_through_storage_projection_dto_and_grpc()
    {
        var message = new UserCreated(Guid.NewGuid(), "Owner", "1234567890");
        var context = Substitute.For<ConsumeContext<UserCreated>>();
        context.Message.Returns(message);
        var dispatcher = Substitute.For<IEventDispatcher>();
        PassengerCreatedDomainEvent created = null;
        dispatcher
            .SendAsync(
                Arg.Do<PassengerCreatedDomainEvent>(value => created = value),
                typeof(IInternalCommand),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        await Fixture.ExecuteDbContextAsync(db =>
        {
            var consumer = new RegisterNewUserHandler(
                db,
                dispatcher,
                NullLogger<RegisterNewUserHandler>.Instance,
                Options.Create(new AppOptions())
            );
            return consumer.Consume(context);
        });

        created.Should().NotBeNull();
        created.UserId.Should().Be(message.Id);
        var persisted = await Fixture.ExecuteDbContextAsync(db =>
            db.Passengers.SingleAsync(passenger => passenger.UserId == message.Id)
        );
        persisted.UserId.Should().Be(message.Id);

        var projection = (CompleteRegisterPassengerMongoCommand)
            new PassengerEventMapper().MapToInternalCommand(created);
        await Fixture.SendAsync(projection);
        var result = await Fixture.SendAsync(new GetPassengerById(created.Id));
        result.PassengerDto.UserId.Should().Be(message.Id);

        var client = new PassengerGrpcService.PassengerGrpcServiceClient(Fixture.Channel);
        var response = await client.GetByIdAsync(new GetByIdRequest { Id = created.Id.ToString() });
        response.PassengerDto.UserId.Should().Be(message.Id.ToString());

        var nonOwner = Fixture.CreateHttpClient(IdentityConstant.Role.User, Guid.NewGuid());
        var forbidden = await nonOwner.GetAsync($"api/v1.0/passenger/{created.Id}");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbidden.Content.Headers.ContentType.MediaType.Should().Be("application/problem+json");
        var problem = await forbidden.Content.ReadFromJsonAsync<ProblemDetails>();
        problem.Status.Should().Be(403);
    }
}
