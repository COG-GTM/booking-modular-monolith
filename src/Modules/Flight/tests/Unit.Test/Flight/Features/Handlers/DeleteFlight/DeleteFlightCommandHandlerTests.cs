namespace Unit.Test.Flight.Features.Handlers.DeleteFlight;

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using global::Flight.Flights.Features.DeletingFlight.V1;
using Microsoft.EntityFrameworkCore;
using Unit.Test.Common;
using Xunit;

[Collection(nameof(UnitTestFixture))]
public class DeleteFlightCommandHandlerTests
{
    private readonly UnitTestFixture _fixture;
    private readonly DeleteFlightHandler _handler;

    public Task<DeleteFlightResult> Act(DeleteFlight command, CancellationToken cancellationToken) =>
        _handler.Handle(command, cancellationToken);

    public DeleteFlightCommandHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
        _handler = new DeleteFlightHandler(fixture.DbContext);
    }

    [Fact]
    public async Task handler_with_valid_command_should_delete_flight_and_return_correct_id()
    {
        // Arrange
        var existingFlight = await _fixture.DbContext.Flights.FirstOrDefaultAsync();
        existingFlight.Should().NotBeNull();

        var command = new DeleteFlight(existingFlight!.Id);

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Id.Should().Be(existingFlight.Id);
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        DeleteFlight command = null;

        // Act
        Func<Task> act = async () => { await Act(command, CancellationToken.None); };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
