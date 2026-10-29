using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BuildingBlocks.HealthCheck;

// Exposes the latest health check results as a gauge so Grafana can show per-service health next to traffic.
public sealed class HealthCheckMetricsPublisher : IHealthCheckPublisher, IDisposable
{
    public const string MeterName = "BuildingBlocks.HealthCheck";
    public const string StatusInstrumentName = "booking.health.status";

    private readonly Meter _meter;
    private readonly ConcurrentDictionary<string, HealthStatus> _latest = new();

    public HealthCheckMetricsPublisher(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);
        _meter.CreateObservableGauge(
            StatusInstrumentName,
            Observe,
            unit: "{status}",
            description: "Latest health check result per check: 1 healthy, 0.5 degraded, 0 unhealthy."
        );
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        foreach (var (name, entry) in report.Entries)
            _latest[name] = entry.Status;

        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<string, HealthStatus> Latest => _latest;

    public static double ToGaugeValue(HealthStatus status) =>
        status switch
        {
            HealthStatus.Healthy => 1,
            HealthStatus.Degraded => 0.5,
            _ => 0,
        };

    private IEnumerable<Measurement<double>> Observe() =>
        _latest.Select(entry => new Measurement<double>(
            ToGaugeValue(entry.Value),
            new KeyValuePair<string, object?>("check", entry.Key)
        ));

    public void Dispose() => _meter.Dispose();
}

public static class HealthCheckMetricsExtensions
{
    public static IServiceCollection AddHealthCheckMetrics(this IServiceCollection services)
    {
        services.AddMetrics();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthCheckPublisher, HealthCheckMetricsPublisher>());
        services.Configure<HealthCheckPublisherOptions>(options =>
        {
            options.Delay = TimeSpan.FromSeconds(10);
            options.Period = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
