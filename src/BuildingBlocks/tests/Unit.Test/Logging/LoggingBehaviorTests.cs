using BuildingBlocks.Logging;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.Logging;

public class LoggingBehaviorTests
{
    private readonly RecordingLogger<LoggingBehavior<FakeCommand, string>> _logger = new();
    private readonly LoggingBehavior<FakeCommand, string> _sut;

    public LoggingBehaviorTests()
    {
        _sut = new LoggingBehavior<FakeCommand, string>(_logger);
    }

    [Fact]
    public async Task fast_request_should_log_start_and_end_and_return_handler_response()
    {
        var response = await _sut.Handle(
            new FakeCommand("fast"),
            TransactionBehaviorFixture.Handler("ok"),
            CancellationToken.None
        );

        response.Should().Be("ok");
        _logger.Entries.Select(e => e.Level).Should().Equal(LogLevel.Information, LogLevel.Information);
        _logger
            .Entries[0]
            .Message.Should()
            .Contain("LoggingBehavior")
            .And.Contain(nameof(FakeCommand))
            .And.Contain(nameof(String));
        _logger.Entries[1].Message.Should().Contain("Handled").And.Contain(nameof(FakeCommand));
    }

    [Fact]
    public async Task handler_failure_should_propagate_and_not_log_completion()
    {
        var act = () =>
            _sut.Handle(
                new FakeCommand("boom"),
                TransactionBehaviorFixture.FailingHandler<string>(new InvalidOperationException("handler failed")),
                CancellationToken.None
            );

        await act.Should().ThrowAsync<InvalidOperationException>();
        _logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task slow_request_should_log_performance_warning()
    {
        var response = await _sut.Handle(
            new FakeCommand("slow"),
            async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(4.1));
                return "slow-ok";
            },
            CancellationToken.None
        );

        response.Should().Be("slow-ok");
        _logger
            .Entries.Select(e => e.Level)
            .Should()
            .Equal(LogLevel.Information, LogLevel.Warning, LogLevel.Information);
        _logger.Entries[1].Message.Should().Contain(nameof(FakeCommand)).And.Contain("took 4 seconds");
    }
}
