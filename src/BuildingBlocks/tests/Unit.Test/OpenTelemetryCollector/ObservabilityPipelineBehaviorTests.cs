using System.Diagnostics;
using System.Diagnostics.Metrics;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.OpenTelemetryCollector.Behaviors;
using BuildingBlocks.OpenTelemetryCollector.CoreDiagnostics.Commands;
using BuildingBlocks.OpenTelemetryCollector.CoreDiagnostics.Query;
using BuildingBlocks.OpenTelemetryCollector.DiagnosticsProvider;
using FluentAssertions;
using MediatR;
using NSubstitute;
using Unit.Test.Common;
using Unit.Test.Fakes;
using Xunit;

namespace Unit.Test.OpenTelemetryCollector;

/// <summary>
/// Command and query diagnostics get their own <see cref="IDiagnosticsProvider"/> (and therefore their own
/// <see cref="Meter"/>) so the test can tell command metrics/activities from query ones by source.
/// </summary>
public sealed class ObservabilityPipelineBehaviorTests : IDisposable
{
    private const string CommandMeterName = "bb-unit-command-diagnostics";
    private const string QueryMeterName = "bb-unit-query-diagnostics";

    private readonly Meter _commandMeter = new(CommandMeterName);
    private readonly Meter _queryMeter = new(QueryMeterName);
    private readonly MeterListener _listener = new();
    private readonly List<(string Meter, string Instrument, long Value)> _measurements = new();
    private readonly List<(string Source, string Activity)> _activities = new();
    private readonly IDiagnosticsProvider _commandDiagnostics = Substitute.For<IDiagnosticsProvider>();
    private readonly IDiagnosticsProvider _queryDiagnostics = Substitute.For<IDiagnosticsProvider>();

    public ObservabilityPipelineBehaviorTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == _commandMeter || instrument.Meter == _queryMeter)
                listener.EnableMeasurementEvents(instrument);
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, value, _, _) => _measurements.Add((instrument.Meter.Name, instrument.Name, value))
        );
        _listener.SetMeasurementEventCallback<double>(
            (instrument, value, _, _) => _measurements.Add((instrument.Meter.Name, instrument.Name, (long)value))
        );
        _listener.Start();

        ConfigureProvider(_commandDiagnostics, _commandMeter);
        ConfigureProvider(_queryDiagnostics, _queryMeter);
    }

    private void ConfigureProvider(IDiagnosticsProvider provider, Meter meter)
    {
        provider.Meter.Returns(meter);
        provider
            .ExecuteActivityAsync(
                Arg.Any<CreateActivityInfo>(),
                Arg.Any<Func<Activity?, CancellationToken, Task<string>>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
            {
                _activities.Add((meter.Name, ci.ArgAt<CreateActivityInfo>(0).Name));
                return ci.ArgAt<Func<Activity?, CancellationToken, Task<string>>>(1)(
                    null,
                    ci.ArgAt<CancellationToken>(2)
                );
            });
    }

    public void Dispose()
    {
        _listener.Dispose();
        _commandMeter.Dispose();
        _queryMeter.Dispose();
    }

    private ObservabilityPipelineBehavior<TRequest, string> CreateSut<TRequest>()
        where TRequest : IRequest<string>
    {
        return new ObservabilityPipelineBehavior<TRequest, string>(
            new CommandHandlerActivity(_commandDiagnostics),
            new CommandHandlerMetrics(_commandDiagnostics),
            new QueryHandlerActivity(_queryDiagnostics),
            new QueryHandlerMetrics(_queryDiagnostics)
        );
    }

    private IEnumerable<string> InstrumentsOf(string meter) =>
        _measurements.Where(m => m.Meter == meter).Select(m => m.Instrument);

    [Fact]
    public async Task command_should_run_inside_command_activity_and_record_command_metrics_only()
    {
        var invoked = false;

        var response = await CreateSut<FakeCommand>()
            .Handle(
                new FakeCommand("cmd"),
                TransactionBehaviorFixture.Handler("done", () => invoked = true),
                CancellationToken.None
            );

        response.Should().Be("done");
        invoked.Should().BeTrue();
        var activity = _activities.Should().ContainSingle().Which;
        activity.Source.Should().Be(CommandMeterName);
        activity
            .Activity.Should()
            .StartWith(ObservabilityConstant.Components.CommandHandler)
            .And.EndWith(nameof(FakeCommand));
        InstrumentsOf(CommandMeterName)
            .Should()
            .Contain(
                new[]
                {
                    TelemetryTags.Metrics.Application.Commands.TotalExecutedCount,
                    TelemetryTags.Metrics.Application.Commands.ActiveCount,
                    TelemetryTags.Metrics.Application.Commands.SuccessCount,
                    TelemetryTags.Metrics.Application.Commands.HandlerDuration,
                }
            );
        InstrumentsOf(CommandMeterName).Should().NotContain(TelemetryTags.Metrics.Application.Commands.FaildCount);
        InstrumentsOf(QueryMeterName).Should().BeEmpty();
        _measurements
            .Where(m =>
                m.Meter == CommandMeterName && m.Instrument == TelemetryTags.Metrics.Application.Commands.ActiveCount
            )
            .Select(m => m.Value)
            .Should()
            .Equal(1, -1);
    }

    [Fact]
    public async Task query_should_run_inside_query_activity_and_record_query_metrics_only()
    {
        var response = await CreateSut<FakeQuery>()
            .Handle(new FakeQuery("qry"), TransactionBehaviorFixture.Handler("result"), CancellationToken.None);

        response.Should().Be("result");
        var activity = _activities.Should().ContainSingle().Which;
        activity.Source.Should().Be(QueryMeterName);
        activity
            .Activity.Should()
            .StartWith(ObservabilityConstant.Components.QueryHandler)
            .And.EndWith(nameof(FakeQuery));
        InstrumentsOf(QueryMeterName)
            .Should()
            .HaveCountGreaterThanOrEqualTo(4)
            .And.OnlyContain(name => !name.EndsWith("failed.count"));
        InstrumentsOf(CommandMeterName).Should().BeEmpty();
        _measurements
            .Where(m => m.Meter == QueryMeterName && m.Instrument.EndsWith("active.count"))
            .Select(m => m.Value)
            .Should()
            .Equal(1, -1);
    }

    [Fact]
    public async Task failing_command_should_rethrow_and_record_command_failure_metric()
    {
        var act = () =>
            CreateSut<FakeCommand>()
                .Handle(
                    new FakeCommand("cmd"),
                    TransactionBehaviorFixture.FailingHandler<string>(new InvalidOperationException("command failed")),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("command failed");
        InstrumentsOf(CommandMeterName).Should().Contain(TelemetryTags.Metrics.Application.Commands.FaildCount);
        InstrumentsOf(CommandMeterName).Should().NotContain(TelemetryTags.Metrics.Application.Commands.SuccessCount);
        InstrumentsOf(QueryMeterName).Should().BeEmpty();
    }

    [Fact]
    public async Task failing_query_should_rethrow_and_record_query_failure_metric()
    {
        var act = () =>
            CreateSut<FakeQuery>()
                .Handle(
                    new FakeQuery("qry"),
                    TransactionBehaviorFixture.FailingHandler<string>(new InvalidOperationException("query failed")),
                    CancellationToken.None
                );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("query failed");
        InstrumentsOf(QueryMeterName).Should().Contain(name => name.EndsWith("failed.count"));
        InstrumentsOf(QueryMeterName).Should().NotContain(name => name.EndsWith("success.count"));
        InstrumentsOf(CommandMeterName).Should().BeEmpty();
    }

    [Fact]
    public async Task plain_request_should_pass_through_without_activity_or_metrics()
    {
        var response = await CreateSut<FakePlainRequest>()
            .Handle(
                new FakePlainRequest("plain"),
                TransactionBehaviorFixture.Handler("plain-result"),
                CancellationToken.None
            );

        response.Should().Be("plain-result");
        _activities.Should().BeEmpty();
        _measurements.Should().BeEmpty();
    }
}
