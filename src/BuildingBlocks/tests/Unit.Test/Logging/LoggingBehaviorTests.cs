using BuildingBlocks.Logging;
using BuildingBlocks.Unit.Test.Support;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BuildingBlocks.Unit.Test.LoggingTests;

public class LoggingBehaviorTests
{
    public sealed record LoggingPingRequest() : IRequest<string>;

    [Fact]
    public async Task returns_the_next_response_and_logs_the_request_type_twice()
    {
        var logger = new RecordingLogger<LoggingBehavior<LoggingPingRequest, string>>();
        var sut = new LoggingBehavior<LoggingPingRequest, string>(logger);

        var result = await sut.Handle(new LoggingPingRequest(), _ => Task.FromResult("pong"), CancellationToken.None);

        result.Should().Be("pong");
        var info = logger.Entries.Where(e => e.Level == LogLevel.Information).ToList();
        info.Should().HaveCount(2);
        info.Should().OnlyContain(e => e.Message.Contains(nameof(LoggingPingRequest)));
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }
}
