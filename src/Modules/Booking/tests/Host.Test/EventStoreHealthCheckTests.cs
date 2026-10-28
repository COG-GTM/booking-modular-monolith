using System.Net;
using Booking.Host.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Host.Test;

public class EventStoreHealthCheckTests
{
    [Theory]
    [InlineData("esdb://localhost:2113?tls=false", "http://localhost:2113/health/live")]
    [InlineData("esdb://localhost?tls=false", "http://localhost:2113/health/live")]
    [InlineData("esdb://localhost:2113?tls=true", "https://localhost:2113/health/live")]
    [InlineData("esdb://localhost:2113", "https://localhost:2113/health/live")]
    public async Task ConnectionStringDeterminesHealthEndpoint(string connectionString, string expectedAddress)
    {
        var handler = SuccessHandler();
        var healthCheck = CreateHealthCheck(handler, ("EventStoreOptions:ConnectionString", connectionString));

        var result = await CheckAsync(healthCheck);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(expectedAddress, handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task ConnectionStringsEventstoreTakesPrecedenceOverEventStoreOptions()
    {
        var handler = SuccessHandler();
        var healthCheck = CreateHealthCheck(
            handler,
            ("ConnectionStrings:eventstore", "esdb://preferred.example:2113?tls=false"),
            ("EventStoreOptions:ConnectionString", "esdb://fallback.example:2113?tls=false")
        );

        var result = await CheckAsync(healthCheck);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("http://preferred.example:2113/health/live", handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task InvalidConnectionStringIsUnhealthyWithoutSendingARequest()
    {
        var handler = SuccessHandler();
        var healthCheck = CreateHealthCheck(handler, ("EventStoreOptions:ConnectionString", "not a connection string"));

        var result = await CheckAsync(healthCheck);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("EventStoreDB connection string is invalid.", result.Description);
        Assert.Null(handler.RequestUri);
    }

    [Fact]
    public async Task NonSuccessResponseIncludesStatusCode()
    {
        var handler = new RecordingHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
        );
        var healthCheck = CreateHealthCheck(
            handler,
            ("EventStoreOptions:ConnectionString", "esdb://localhost?tls=false")
        );

        var result = await CheckAsync(healthCheck);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("EventStoreDB returned 503.", result.Description);
        Assert.Equal("http://localhost:2113/health/live", handler.RequestUri?.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RequestExceptionsReportEndpointUnavailable(bool taskCanceled)
    {
        var handler = new RecordingHandler(
            (_, _) =>
                Task.FromException<HttpResponseMessage>(
                    taskCanceled
                        ? new TaskCanceledException("Timed out.")
                        : new HttpRequestException("Connection refused.")
                )
        );
        var healthCheck = CreateHealthCheck(
            handler,
            ("EventStoreOptions:ConnectionString", "esdb://localhost?tls=false")
        );

        var result = await CheckAsync(healthCheck);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("EventStoreDB health endpoint is unavailable.", result.Description);
        Assert.NotNull(handler.RequestUri);
    }

    private static EventStoreHealthCheck CreateHealthCheck(
        RecordingHandler handler,
        params (string Key, string Value)[] settings
    )
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value))
            .Build();
        return new EventStoreHealthCheck(new HttpClient(handler), configuration);
    }

    private static RecordingHandler SuccessHandler() =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

    private static Task<HealthCheckResult> CheckAsync(EventStoreHealthCheck healthCheck) =>
        healthCheck.CheckHealthAsync(new HealthCheckContext());

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond
    ) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            RequestUri = request.RequestUri;
            return respond(request, cancellationToken);
        }
    }
}
