using FluentAssertions;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Passenger.Features.Handlers.CompleteRegisterPassenger;

using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Exceptions;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

[Collection(nameof(UnitTestFixture))]
public class CompleteRegisterPassengerCommandHandlerTests
{
    private readonly UnitTestFixture _fixture;

    public CompleteRegisterPassengerCommandHandlerTests(UnitTestFixture fixture)
    {
        _fixture = fixture;
    }

    private Task<CompleteRegisterPassengerResult> Act(
        global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1.CompleteRegisterPassenger command,
        CancellationToken cancellationToken
    )
    {
        var handler = new CompleteRegisterPassengerCommandHandler(_fixture.Mapper, _fixture.DbContext);
        return handler.Handle(command, cancellationToken);
    }

    [Fact]
    public async Task handler_with_existing_passport_should_complete_registration()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand().Generate();

        // Act
        var response = await Act(command, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.PassengerDto.Id.Should().Be(FakePassengerCreate.SeededPassengerId);
        response.PassengerDto.PassportNumber.Should().Be(command.PassportNumber);
        response.PassengerDto.PassengerType.Should().Be(PassengerType.Male);
        response.PassengerDto.Age.Should().Be(30);
    }

    [Fact]
    public async Task handler_with_unknown_passport_should_throw_passenger_not_exist()
    {
        // Arrange
        var command = new FakeCompleteRegisterPassengerCommand("000000000").Generate();

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<PassengerNotExist>();
    }

    [Fact]
    public async Task handler_with_null_command_should_throw_argument_exception()
    {
        // Arrange
        global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1.CompleteRegisterPassenger command = null;

        // Act
        Func<Task> act = async () =>
        {
            await Act(command, CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
