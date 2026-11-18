using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Integration.Test.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Integration.Test.Passenger.Infrastructure;

using global::Passenger.Data;
using global::Passenger.Passengers.Dtos;
using global::Passenger.Passengers.Enums;
using global::Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

public class EfTxPassengerBehaviorLoggingTests
{
    [Fact]
    public async Task handle_should_not_log_complete_register_passenger_passport_number()
    {
        var command = new CompleteRegisterPassenger("PX-998877", PassengerType.Male, 42);
        var expected = new CompleteRegisterPassengerResult(
            new PassengerDto(Guid.NewGuid(), "Jane Doe", command.PassportNumber, command.PassengerType, command.Age)
        );

        var logger =
            new CapturingLogger<EfTxPassengerBehavior<CompleteRegisterPassenger, CompleteRegisterPassengerResult>>();
        await using var passengerDbContext = new PassengerDbContext(
            new DbContextOptionsBuilder<PassengerDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
        );

        var behavior = new EfTxPassengerBehavior<CompleteRegisterPassenger, CompleteRegisterPassengerResult>(
            logger,
            passengerDbContext,
            Substitute.For<IPersistMessageDbContext>(),
            Substitute.For<IEventDispatcher>()
        );

        var response = await behavior.Handle(command, _ => Task.FromResult(expected), CancellationToken.None);

        response.Should().BeSameAs(expected);
        logger.Entries.Should().NotBeEmpty();
        logger.Entries.Should().NotContain(e => e.Message.Contains(command.PassportNumber));
        logger.Entries.SelectMany(e => e.State).Should().NotContain(p => p.Key == "RequestContent");
        logger
            .Entries.Should()
            .Contain(e =>
                e.Level == LogLevel.Information
                && e.State.Any(p =>
                    p.Key == "MediatrRequest" && Equals(p.Value, typeof(CompleteRegisterPassenger).FullName)
                )
            );
    }
}
