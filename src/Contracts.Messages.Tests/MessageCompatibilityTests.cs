using System.Reflection;
using System.Text.Json;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core.Event;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Contracts.Messages.Tests;

public class MessageCompatibilityTests
{
    [Theory]
    [InlineData("FlightCreated")]
    [InlineData("FlightUpdated")]
    [InlineData("FlightDeleted")]
    [InlineData("AircraftCreated")]
    [InlineData("AirportCreated")]
    [InlineData("SeatCreated")]
    [InlineData("SeatReserved")]
    [InlineData("PassengerRegistrationCompleted")]
    [InlineData("PassengerCreated")]
    [InlineData("BookingCreated")]
    public void V1_messages_keep_their_names_constructor_and_id_payload(string name)
    {
        var type = typeof(UserCreated).Assembly.GetType(
            $"BuildingBlocks.Contracts.EventBus.Messages.{name}",
            throwOnError: true
        )!;
        Assert.True(typeof(IIntegrationEvent).IsAssignableFrom(type));
        Assert.Equal(typeof(Guid), type.GetProperty("Id")?.PropertyType);
        var id = Guid.Parse("827d0a11-1111-4444-8888-111111111111");
        var message = type.GetConstructor([typeof(Guid)])!.Invoke([id]);
        Assert.Equal($"{{\"Id\":\"{id}\"}}", JsonSerializer.Serialize(message, type));
        Assert.Equal(
            $"urn:message:BuildingBlocks.Contracts.EventBus.Messages:{name}",
            MessageUrn.ForType(type).ToString()
        );
    }

    [Fact]
    public void V1_user_created_keeps_its_payload_and_can_read_old_json()
    {
        const string json =
            "{\"Id\":\"827d0a11-1111-4444-8888-111111111111\",\"Name\":\"Sam Passenger\",\"PassportNumber\":\"AB123456\"}";
        var message = new UserCreated(Guid.Parse("827d0a11-1111-4444-8888-111111111111"), "Sam Passenger", "AB123456");
        Assert.Equal(json, JsonSerializer.Serialize(message));
        Assert.Equal(message, JsonSerializer.Deserialize<UserCreated>(json));
        Assert.Equal(typeof(string), typeof(UserCreated).GetProperty(nameof(UserCreated.Name))?.PropertyType);
        Assert.Equal(typeof(string), typeof(UserCreated).GetProperty(nameof(UserCreated.PassportNumber))?.PropertyType);
        Assert.Equal(
            "urn:message:BuildingBlocks.Contracts.EventBus.Messages:UserCreated",
            MessageUrn.ForType<UserCreated>().ToString()
        );
    }

    [Fact]
    public void Package_is_independent_of_building_blocks_and_heavy_infrastructure()
    {
        var assembly = typeof(UserCreated).Assembly;
        Assert.Equal("Contracts.Messages", assembly.GetName().Name);
        Assert.Equal(assembly, typeof(IEvent).Assembly);
        Assert.Equal(assembly, typeof(IIntegrationEvent).Assembly);
        Assert.All(
            assembly.GetReferencedAssemblies(),
            reference =>
                Assert.True(
                    reference.Name!.StartsWith("System.", StringComparison.Ordinal)
                        || reference.Name is "MassTransit.Abstractions" or "MediatR.Contracts",
                    reference.FullName
                )
        );
        Assert.NotNull(typeof(IIntegrationEvent).GetCustomAttribute<ExcludeFromTopologyAttribute>());
    }

    [Fact]
    public async Task Packaged_user_created_round_trips_through_mass_transit()
    {
        var received = new TaskCompletionSource<UserCreated>(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMassTransit(configure =>
            configure.UsingInMemory(
                (_, bus) =>
                    bus.ReceiveEndpoint(
                        "contracts-user-created",
                        endpoint =>
                            endpoint.Handler<UserCreated>(context =>
                            {
                                received.TrySetResult(context.Message);
                                return Task.CompletedTask;
                            })
                    )
            )
        );
        await using var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        try
        {
            var message = new UserCreated(Guid.NewGuid(), "Sam Passenger", "AB123456");
            await bus.Publish(message);
            Assert.Equal(message, await received.Task.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            await bus.StopAsync();
        }
    }
}
