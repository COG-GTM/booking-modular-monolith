using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Unit.Test.Common;
using Xunit;

namespace Unit.Test.Data;

using global::Flight.Data;

public class EfTxFlightBehaviorLoggingTests
{
    private const string Password = "S3cr3t-P@ssw0rd!";
    private const string PassportNumber = "PX-998877";

    public sealed record SensitiveFlightCommand(string Password, string PassportNumber) : IRequest<string>;

    [Fact]
    public async Task handle_should_not_log_serialized_request_content_at_debug_level()
    {
        var logger = new CapturingLogger<EfTxFlightBehavior<SensitiveFlightCommand, string>>();
        await using var flightDbContext = new FlightDbContext(
            new DbContextOptionsBuilder<FlightDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options
        );

        var behavior = new EfTxFlightBehavior<SensitiveFlightCommand, string>(
            logger,
            flightDbContext,
            Substitute.For<IPersistMessageDbContext>(),
            Substitute.For<IEventDispatcher>()
        );

        var response = await behavior.Handle(
            new SensitiveFlightCommand(Password, PassportNumber),
            _ => Task.FromResult("handled"),
            CancellationToken.None
        );

        response.Should().Be("handled");
        logger.Entries.Should().NotBeEmpty();
        logger.Entries.Should().NotContain(e => e.Message.Contains(Password) || e.Message.Contains(PassportNumber));
        logger.Entries.SelectMany(e => e.State).Should().NotContain(p => p.Key == "RequestContent");
        logger
            .Entries.Should()
            .Contain(e =>
                e.Level == LogLevel.Information
                && e.State.Any(p =>
                    p.Key == "MediatrRequest" && Equals(p.Value, typeof(SensitiveFlightCommand).FullName)
                )
            );
    }
}
