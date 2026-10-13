using System.Net;
using BuildingBlocks.HealthCheck;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace BuildingBlocks.Unit.Test;

public class EventStoreDbHealthCheckProbeTests
{
    private const string ConnectionString = "esdb://admin:changeit@eventstore:2113?tls=false";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri?> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request.RequestUri);
            return Task.FromResult(respond(request));
        }
    }

    private static HealthCheckContext Context(HealthStatus failureStatus = HealthStatus.Unhealthy) =>
        new()
        {
            Registration = new HealthCheckRegistration("eventstore", _ => null!, failureStatus, [Extensions.ReadyTag]),
        };

    private static (EventStoreDbHealthCheck Check, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        Func<string?>? connectionString = null
    )
    {
        var handler = new StubHandler(respond);
        var check = new EventStoreDbHealthCheck(connectionString ?? (() => ConnectionString), new HttpClient(handler));

        return (check, handler);
    }

    [Fact]
    public async Task healthy_when_the_node_liveness_endpoint_answers_with_success()
    {
        var (check, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var result = await check.CheckHealthAsync(Context());

        result.Status.Should().Be(HealthStatus.Healthy);
        handler.Requests.Should().ContainSingle().Which.Should().Be(new Uri("http://eventstore:2113/health/live"));
    }

    [Theory]
    [InlineData(HealthStatus.Unhealthy)]
    [InlineData(HealthStatus.Degraded)]
    public async Task non_success_liveness_response_reports_the_registration_failure_status(HealthStatus failureStatus)
    {
        var (check, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await check.CheckHealthAsync(Context(failureStatus));

        result.Status.Should().Be(failureStatus);
        result.Description.Should().Contain("503");
    }

    [Fact]
    public async Task unreachable_node_reports_failure_with_the_exception_instead_of_throwing()
    {
        var (check, _) = Create(_ => throw new HttpRequestException("connection refused"));

        var result = await check.CheckHealthAsync(Context());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task timed_out_liveness_request_reports_failure_instead_of_throwing()
    {
        var (check, _) = Create(_ => throw new TaskCanceledException("timed out"));

        var result = await check.CheckHealthAsync(Context(HealthStatus.Degraded));

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Exception.Should().BeOfType<TaskCanceledException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("esdb://")]
    [InlineData("admin:changeit@eventstore:1113")]
    public async Task invalid_connection_string_fails_without_calling_the_node(string? connectionString)
    {
        var (check, handler) = Create(
            _ => new HttpResponseMessage(HttpStatusCode.OK),
            connectionString: () => connectionString
        );

        var result = await check.CheckHealthAsync(Context());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Be("Invalid EventStoreDB connection string.");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task connection_string_is_re_read_on_every_check()
    {
        var current = "esdb://node-a:2113?tls=false";
        var (check, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK), connectionString: () => current);

        await check.CheckHealthAsync(Context());
        current = "esdb://node-b:2113?tls=true";
        await check.CheckHealthAsync(Context());

        handler
            .Requests.Should()
            .Equal(new Uri("http://node-a:2113/health/live"), new Uri("https://node-b:2113/health/live"));
    }

    [Fact]
    public async Task configured_eventstore_readiness_check_reports_unhealthy_when_no_node_is_listening()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EventStoreOptions:ConnectionString"] = "esdb://127.0.0.1:1?tls=false",
                }
            )
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddCustomHealthCheck();
        await using var provider = services.BuildServiceProvider();

        var report = await provider
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(r => r.Name == "eventstore");

        report.Status.Should().Be(HealthStatus.Unhealthy);
        report.Entries.Should().ContainKey("eventstore").WhoseValue.Tags.Should().Contain(Extensions.ReadyTag);
    }
}
