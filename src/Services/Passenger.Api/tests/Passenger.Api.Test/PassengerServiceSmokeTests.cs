using System;
using System.Net;
using System.Threading.Tasks;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using FluentAssertions;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Xunit;

namespace Passenger.Api.Test;

public class PassengerServiceSmokeTests : PassengerApiTestBase
{
    public PassengerServiceSmokeTests(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }

    [Fact]
    public async Task root_should_return_the_service_name()
    {
        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Passenger-Service");
    }

    [Fact]
    public async Task unknown_passenger_should_return_not_found()
    {
        var response = await Fixture.HttpClient.GetAsync($"/api/v1.0/passenger/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task user_created_from_the_broker_should_create_a_passenger()
    {
        var passportNumber = $"P{NewId.NextGuid().ToString("N")[..8]}";

        await Fixture.Publish(new UserCreated(NewId.NextGuid(), "Smoke Tester", passportNumber));

        var created = false;
        var deadline = DateTime.UtcNow.AddSeconds(60);

        while (!created && DateTime.UtcNow < deadline)
        {
            created = await Fixture.ExecuteDbContextAsync(db =>
                db.Passengers.AnyAsync(x => x.PassportNumber.Value == passportNumber)
            );

            if (!created)
            {
                await Task.Delay(250);
            }
        }

        created.Should().BeTrue("the Passenger host must consume UserCreated from the message broker");
    }

    [Fact]
    public void host_should_only_wire_the_passenger_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventMapper>().Should().BeOfType<PassengerEventMapper>();
    }
}
