using Api;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.TestBase;
using BuildingBlocks.Utils;
using Identity.Identity.Features.RegisteringNewUser.V1;
using Identity.Identity.Models;
using MassTransit;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Xunit;

namespace Integration.Test.Passenger.Features;

public class PackagedUserCreatedTests(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> fixture)
    : PassengerIntegrationTestBase(fixture)
{
    [Fact]
    public async Task Identity_registration_creates_passenger_using_packaged_contract()
    {
        Assert.Equal("Contracts.Messages", typeof(UserCreated).Assembly.GetName().Name);
        Assert.Equal(
            typeof(UserCreated),
            TypeProvider.GetFirstMatchingTypeFromCurrentDomainAssembly(
                "BuildingBlocks.Contracts.EventBus.Messages.UserCreated"
            )
        );
        Assert.Equal(
            typeof(UserCreated),
            Type.GetType("BuildingBlocks.Contracts.EventBus.Messages.UserCreated, BuildingBlocks", throwOnError: true)
        );
        Assert.Equal(
            typeof(IIntegrationEvent),
            Type.GetType("BuildingBlocks.Core.Event.IIntegrationEvent, BuildingBlocks", throwOnError: true)
        );

        var suffix = Guid.NewGuid().ToString("N");
        await using (var scope = Fixture.ServiceProvider.CreateAsyncScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
            if (!await roles.RoleExistsAsync("user"))
            {
                var created = await roles.CreateAsync(new Role { Name = "user" });
                Assert.True(created.Succeeded);
            }
        }

        var passport = suffix[..9];
        var received = new TaskCompletionSource<UserCreated>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var observer = Fixture
            .ServiceProvider.GetRequiredService<IBus>()
            .ConnectConsumeObserver(new UserCreatedObserver(passport, received));
        var command = new RegisterNewUser(
            "Sam",
            "Passenger",
            $"sam{suffix}",
            $"sam{suffix}@example.com",
            "Pass123$",
            "Pass123$",
            passport
        );

        var result = await Fixture.SendAsync(command);
        Assert.NotNull(result);

        await using (var scope = Fixture.ServiceProvider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPersistMessageProcessor>().ProcessAllAsync();
        }

        var consumed = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(result.Id, consumed.Id);
        Assert.Equal("Sam Passenger", consumed.Name);
        var passenger = await Fixture.ExecuteDbContextAsync(context =>
            context.Passengers.SingleAsync(x => x.PassportNumber.Value == passport)
        );
        Assert.Equal("Sam Passenger", passenger.Name.Value);
        Assert.Equal(passport, passenger.PassportNumber.Value);
    }

    private sealed class UserCreatedObserver(string passport, TaskCompletionSource<UserCreated> received)
        : IConsumeObserver
    {
        public Task PreConsume<T>(ConsumeContext<T> context)
            where T : class => Task.CompletedTask;

        public Task PostConsume<T>(ConsumeContext<T> context)
            where T : class
        {
            if (context.Message is UserCreated message && message.PassportNumber == passport)
                received.TrySetResult(message);
            return Task.CompletedTask;
        }

        public Task ConsumeFault<T>(ConsumeContext<T> context, Exception exception)
            where T : class
        {
            if (context.Message is UserCreated message && message.PassportNumber == passport)
                received.TrySetException(exception);
            return Task.CompletedTask;
        }
    }
}
