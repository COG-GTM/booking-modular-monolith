using System;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.Exception;
using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Flights.Enums;
using Flight.Flights.Features.UpdatingFlight.V1;
using FluentAssertions;
using Xunit;

namespace Integration.Test.Flight.Features;

public class UpdateFlightValidationTests : FlightIntegrationTestBase
{
    public UpdateFlightValidationTests(TestFixture<Program, FlightDbContext, FlightReadDbContext> fixture)
        : base(fixture) { }

    [Fact]
    public async Task should_throw_validation_exception_through_mediator_when_update_flight_id_is_empty()
    {
        var command = new UpdateFlight(
            Guid.Empty,
            "12FF",
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow.AddHours(1),
            Guid.NewGuid(),
            60,
            DateTime.UtcNow,
            FlightStatus.Flying,
            false,
            800
        );

        Func<Task> act = () => Fixture.SendAsync(command);

        await act.Should().ThrowAsync<ValidationException>().WithMessage("Id must be not empty");
    }
}
