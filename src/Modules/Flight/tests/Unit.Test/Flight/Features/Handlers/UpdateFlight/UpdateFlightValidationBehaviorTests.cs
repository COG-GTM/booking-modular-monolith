namespace Unit.Test.Flight.Features.Handlers.UpdateFlight;

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildingBlocks.Validation;
using FluentAssertions;
using FluentValidation;
using global::Flight.Flights.Features.UpdatingFlight.V1;
using Microsoft.Extensions.DependencyInjection;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;
using ValidationException = BuildingBlocks.Exception.ValidationException;

[Collection(nameof(UnitTestFixture))]
public class UpdateFlightValidationBehaviorTests
{
    [Fact]
    public async Task validation_behavior_should_reject_invalid_update_flight_before_reaching_handler()
    {
        // Arrange
        var command = new FakeValidateUpdateFlightCommand().Generate();
        var behavior = CreateBehavior();
        var handlerCalled = false;

        // Act
        Func<Task> act = () => behavior.Handle(command, _ =>
        {
            handlerCalled = true;
            return Task.FromResult(new UpdateFlightResult(command.Id));
        }, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>();
        handlerCalled.Should().BeFalse();
    }

    [Fact]
    public async Task validation_behavior_should_pass_valid_update_flight_to_handler()
    {
        // Arrange
        var command = new FakeUpdateFlightCommand().Generate();
        var behavior = CreateBehavior();

        // Act
        var result = await behavior.Handle(command,
            _ => Task.FromResult(new UpdateFlightResult(command.Id)), CancellationToken.None);

        // Assert
        result.Id.Should().Be(command.Id);
    }

    private static ValidationBehavior<global::Flight.Flights.Features.UpdatingFlight.V1.UpdateFlight, UpdateFlightResult> CreateBehavior()
    {
        var services = new ServiceCollection();
        services.AddValidatorsFromAssemblyContaining<UpdateFlightValidator>();
        var provider = services.BuildServiceProvider();

        return new ValidationBehavior<global::Flight.Flights.Features.UpdatingFlight.V1.UpdateFlight, UpdateFlightResult>(provider);
    }
}
