using System.Diagnostics;
using System.Net;
using System.Text.Json;
using BuildingBlocks.Core.Event;
using BuildingBlocks.HealthCheck;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.Extensions;
using Xunit;
using HealthExtensions = BuildingBlocks.HealthCheck.Extensions;

namespace BuildingBlocks.Tests;

public class ServiceTelemetryProbeGapTests
{
    private const string RequestTraceParent = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public void per_module_postgres_connection_strings_each_get_a_named_readiness_check()
    {
        using var provider = CreateHealthProvider(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = "Host=flight;Database=flight;Username=u;Password=p",
                ["PostgresOptions:ConnectionString:Passenger"] =
                    "Host=passenger;Database=passenger;Username=u;Password=p",
                ["PostgresOptions:ConnectionString:Identity"] = "",
            }
        );

        var registrations = Registrations(provider);

        registrations
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo(["self", "rabbitmq", "Postgres-Flight", "Postgres-Passenger"]);
        registrations
            .Where(r => r.Name.StartsWith("Postgres-", StringComparison.Ordinal))
            .Should()
            .OnlyContain(r => HealthExtensions.IsReadinessCheck(r) && !HealthExtensions.IsLivenessCheck(r));
    }

    [Fact]
    public void single_postgres_connection_string_keeps_one_unsuffixed_check()
    {
        using var provider = CreateHealthProvider(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString"] = "Host=localhost;Database=test;Username=u;Password=p",
            }
        );

        Registrations(provider)
            .Select(r => r.Name)
            .Should()
            .Contain("npgsql")
            .And.NotContain(name => name.StartsWith("Postgres-", StringComparison.Ordinal));
    }

    [Fact]
    public void only_the_self_check_is_a_liveness_check()
    {
        using var provider = CreateHealthProvider(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = "Host=flight;Database=flight;Username=u;Password=p",
                ["EventStoreOptions:ConnectionString"] = "esdb://localhost:2113",
                ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
            }
        );

        var registrations = Registrations(provider);

        registrations.Where(HealthExtensions.IsLivenessCheck).Select(r => r.Name).Should().Equal("self");
        registrations
            .Where(HealthExtensions.IsReadinessCheck)
            .Select(r => r.Name)
            .Should()
            .BeEquivalentTo(["rabbitmq", "Postgres-Flight", "eventstore", "MongoDB-Health"]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ui_toggle_controls_health_checks_ui_services_but_not_dependency_checks(bool uiEnabled)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["HealthOptions:Enabled"] = "true",
                        ["HealthOptions:UIEnabled"] = uiEnabled.ToString(),
                        ["AppOptions:Name"] = "test-app",
                    }
                )
                .Build()
        );

        services.AddCustomHealthCheck();

        services
            .Any(d => d.ServiceType.Namespace?.StartsWith("HealthChecks.UI", StringComparison.Ordinal) == true)
            .Should()
            .Be(uiEnabled);
        using var provider = services.BuildServiceProvider();
        Registrations(provider).Select(r => r.Name).Should().Contain(["self", "rabbitmq"]);
    }

    [Fact]
    public async Task health_ui_is_not_mapped_when_ui_is_disabled()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["HealthOptions:Enabled"] = "true",
                ["HealthOptions:UIEnabled"] = "false",
                ["HealthOptions:MetricsEnabled"] = "false",
                ["AppOptions:Name"] = "test-app",
            }
        );
        builder.Services.AddCustomHealthCheck();
        await using var app = builder.Build();
        app.UseCustomHealthCheck();
        await app.StartAsync();
        using var client = app.GetTestClient();

        (await client.GetAsync("/health-ui")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(HealthExtensions.LivenessEndpointPath)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public void legacy_misspelt_aspire_dashboard_key_still_binds_and_blank_values_keep_defaults()
    {
        var options = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ObservabilityOptions:AspireDashboardOTLPOptions:OTLPGrpExporterEndpoint"] =
                        "http://aspire-dashboard:18889",
                    ["ObservabilityOptions:OTLPOptions:OTLPGrpExporterEndpoint"] = " ",
                }
            )
            .Build()
            .GetSection(nameof(ObservabilityOptions))
            .Get<ObservabilityOptions>()!;

        options.AspireDashboardOTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://aspire-dashboard:18889");
        options.AspireDashboardOTLPOptions.OTLPGrpExporterEndpoint.Should().BeNull();
        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://localhost:4317");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void blank_otel_exporter_endpoint_keeps_the_configured_collector(string? endpoint)
    {
        var options = new ObservabilityOptions
        {
            OTLPOptions = new OTLPOptions { OTLPGrpcExporterEndpoint = "http://otel-collector:4317" },
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [BuildingBlocks.OpenTelemetryCollector.Extensions.OtelExporterEndpointKey] = endpoint,
                }
            )
            .Build();

        BuildingBlocks.OpenTelemetryCollector.Extensions.ApplyOtlpEndpointOverride(configuration, options);

        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://otel-collector:4317");
    }

    [Fact]
    public void otel_exporter_endpoint_creates_missing_otlp_options()
    {
        var options = new ObservabilityOptions { OTLPOptions = null! };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [BuildingBlocks.OpenTelemetryCollector.Extensions.OtelExporterEndpointKey] =
                        "http://collector:4317",
                }
            )
            .Build();

        BuildingBlocks.OpenTelemetryCollector.Extensions.ApplyOtlpEndpointOverride(configuration, options);

        options.OTLPOptions.OTLPGrpcExporterEndpoint.Should().Be("http://collector:4317");
    }

    [Fact]
    public async Task inbox_rows_are_stored_without_the_current_trace_context()
    {
        await using var store = CreateStore();
        var processor = CreateProcessor(store, Substitute.For<IMediator>(), Substitute.For<IPublishEndpoint>());
        using var activity = new Activity("consume").SetIdFormat(ActivityIdFormat.W3C).Start();

        await processor.AddReceivedMessageAsync(
            new MessageEnvelope(new TracedEvent(Guid.NewGuid()), new Dictionary<string, object?> { ["tenant"] = "one" })
        );

        var row = await store.PersistMessage.SingleAsync();
        row.DeliveryType.Should().Be(MessageDeliveryType.Inbox);
        var headers = JsonSerializer.Deserialize<MessageEnvelope>(row.Data)!.Headers;
        headers.Keys.Should().Equal("tenant");
    }

    [Fact]
    public async Task internal_command_row_carries_the_request_trace_and_processing_rejoins_it()
    {
        using var capture = new ActivityCapture();
        using var requestSource = new ActivitySource(nameof(ServiceTelemetryProbeGapTests));
        await using var store = CreateStore();
        Activity? sendActivity = null;
        var mediator = Substitute.For<IMediator>();
        mediator.ReturnsForAll<Task<object?>>(_ =>
        {
            sendActivity = Activity.Current;
            return Task.FromResult<object?>(null);
        });
        mediator.ReturnsForAll<Task<Unit>>(_ =>
        {
            sendActivity = Activity.Current;
            return Task.FromResult(Unit.Value);
        });
        var processor = CreateProcessor(store, mediator, Substitute.For<IPublishEndpoint>());

        ActivityTraceId requestTraceId;
        ActivitySpanId requestSpanId;
        using (var request = requestSource.StartActivity("POST /api/v1/flight"))
        {
            requestTraceId = request!.TraceId;
            requestSpanId = request.SpanId;
            await processor.AddInternalMessageAsync(new TracedCommand(Guid.NewGuid()));
        }

        var row = await store.PersistMessage.SingleAsync();
        row.DeliveryType.Should().Be(MessageDeliveryType.Internal);
        JsonSerializer.Deserialize<MessageEnvelope>(row.Data)!.Headers[PersistMessageTracing.TraceParentHeader]!
            .ToString()
            .Should()
            .Contain(requestTraceId.ToString());

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Internal);

        sendActivity.Should().NotBeNull();
        sendActivity!.Source.Name.Should().Be(PersistMessageTracing.ActivitySourceName);
        sendActivity.DisplayName.Should().Be($"internal command {nameof(TracedCommand)}");
        sendActivity.Kind.Should().Be(ActivityKind.Internal);
        sendActivity.TraceId.Should().Be(requestTraceId);
        sendActivity.ParentSpanId.Should().Be(requestSpanId);
        sendActivity.GetTagItem("persist_message.delivery_type").Should().Be(nameof(MessageDeliveryType.Internal));
        (await store.PersistMessage.SingleAsync()).MessageStatus.Should().Be(MessageStatus.Processed);
    }

    [Fact]
    public async Task outbox_publish_span_is_a_producer_span_tagged_with_the_stored_message()
    {
        using var capture = new ActivityCapture();
        await using var store = CreateStore();
        Activity? publishActivity = null;
        var endpoint = Substitute.For<IPublishEndpoint>();
        endpoint.ReturnsForAll<Task>(_ =>
        {
            publishActivity = Activity.Current;
            return Task.CompletedTask;
        });
        var processor = CreateProcessor(store, Substitute.For<IMediator>(), endpoint);
        await processor.PublishMessageAsync(
            new MessageEnvelope(
                new TracedEvent(Guid.NewGuid()),
                new Dictionary<string, object?> { [PersistMessageTracing.TraceParentHeader] = RequestTraceParent }
            )
        );
        var row = await store.PersistMessage.SingleAsync();

        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        publishActivity.Should().NotBeNull();
        publishActivity!.Kind.Should().Be(ActivityKind.Producer);
        publishActivity.TraceId.ToString().Should().Be("0af7651916cd43dd8448eb211c80319c");
        publishActivity.ParentSpanId.ToString().Should().Be("b7ad6b7169203331");
        publishActivity.HasRemoteParent.Should().BeTrue();
        publishActivity.GetTagItem("messaging.message.id").Should().Be(row.Id.ToString());
        publishActivity.GetTagItem("messaging.message.type").Should().Be(row.DataType);
        publishActivity.GetTagItem("persist_message.delivery_type").Should().Be(nameof(MessageDeliveryType.Outbox));
        publishActivity.GetTagItem("persist_message.retry_count").Should().Be(row.RetryCount);
        capture.Stopped.Should().Contain(publishActivity);
    }

    [Fact]
    public async Task outbox_publish_without_stored_trace_starts_a_new_root_trace()
    {
        using var capture = new ActivityCapture();
        await using var store = CreateStore();
        Activity? publishActivity = null;
        var endpoint = Substitute.For<IPublishEndpoint>();
        endpoint.ReturnsForAll<Task>(_ =>
        {
            publishActivity = Activity.Current;
            return Task.CompletedTask;
        });
        var processor = CreateProcessor(store, Substitute.For<IMediator>(), endpoint);
        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            await processor.PublishMessageAsync(new MessageEnvelope(new TracedEvent(Guid.NewGuid())));
        }
        finally
        {
            Activity.Current = previous;
        }
        var row = await store.PersistMessage.SingleAsync();
        JsonSerializer
            .Deserialize<MessageEnvelope>(row.Data)!
            .Headers.Should()
            .NotContainKey(PersistMessageTracing.TraceParentHeader);

        Activity.Current = null;
        await processor.ProcessAsync(row.Id, MessageDeliveryType.Outbox);

        publishActivity.Should().NotBeNull();
        publishActivity!.Parent.Should().BeNull();
        publishActivity.ParentSpanId.Should().Be(default(ActivitySpanId));
        publishActivity.HasRemoteParent.Should().BeFalse();
    }

    private static ServiceProvider CreateHealthProvider(Dictionary<string, string?> settings)
    {
        settings.TryAdd("HealthOptions:Enabled", "true");
        settings.TryAdd("HealthOptions:UIEnabled", "false");
        settings.TryAdd("AppOptions:Name", "test-app");
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddCustomHealthCheck();
        return services.BuildServiceProvider();
    }

    private static HealthCheckRegistration[] Registrations(IServiceProvider provider) =>
        provider
            .GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.DistinctBy(r => r.Name)
            .ToArray();

    private static PersistMessageDbContext CreateStore() =>
        new(
            new DbContextOptionsBuilder<PersistMessageDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static global::BuildingBlocks.PersistMessageProcessor.PersistMessageProcessor CreateProcessor(
        PersistMessageDbContext store,
        IMediator mediator,
        IPublishEndpoint endpoint
    ) =>
        new(
            NullLogger<global::BuildingBlocks.PersistMessageProcessor.PersistMessageProcessor>.Instance,
            mediator,
            store,
            endpoint
        );

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivityCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source =>
                    source.Name is PersistMessageTracing.ActivitySourceName or nameof(ServiceTelemetryProbeGapTests),
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity => Stopped.Add(activity),
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Stopped { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed record TracedEvent(Guid EventId) : IIntegrationEvent;

    private sealed record TracedCommand(Guid Id) : InternalCommand;
}
