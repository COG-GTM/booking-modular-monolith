using System.Linq;
using System.Net;
using System.Threading.Tasks;
using AutoBogus;
using BuildingBlocks.Contracts.Grpc.Flight;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Data.Seed;
using Flight.Flights.Features.CreatingFlight.V1;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flight.Api.Test;

public class FlightServiceSmokeTests : FlightApiTestBase
{
    public FlightServiceSmokeTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task root_should_return_the_service_name()
    {
        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Flight-Service");
    }

    [Fact]
    public async Task get_available_flights_should_be_served_by_the_standalone_host()
    {
        await Fixture.SendAsync(NewFlight());

        var response = await Fixture.HttpClient.GetAsync("/api/v1.0/flight/get-available-flights");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task get_flight_by_id_should_be_served_over_grpc()
    {
        var command = NewFlight();

        await Fixture.SendAsync(command);

        var client = new FlightGrpcService.FlightGrpcServiceClient(Fixture.Channel);

        var response = await client.GetByIdAsync(new GetByIdRequest { Id = command.Id.ToString() });

        response.FlightDto.Id.Should().Be(command.Id.ToString());
    }

    [Fact]
    public void host_should_only_wire_the_flight_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventMapper>().Should().BeOfType<FlightEventMapper>();
    }

    private static CreateFlightMongo NewFlight() =>
        new AutoFaker<CreateFlightMongo>()
            .RuleFor(r => r.Id, _ => NewId.NextGuid())
            .RuleFor(r => r.FlightNumber, _ => "SMK1")
            .RuleFor(r => r.DepartureAirportId, _ => InitialData.Airports.First().Id)
            .RuleFor(r => r.ArriveAirportId, _ => InitialData.Airports.Last().Id)
            .RuleFor(r => r.Status, _ => Flights.Enums.FlightStatus.Flying)
            .RuleFor(r => r.AircraftId, _ => InitialData.Aircrafts.First().Id)
            .RuleFor(r => r.IsDeleted, _ => false)
            .Generate();
}
