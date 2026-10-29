namespace BuildingBlocks.HealthCheck;

public class HealthOptions
{
    public bool Enabled { get; set; } = true;
    public bool UIEnabled { get; set; } = true;

    // Publishes every check's result as the booking.health.status gauge (meter BuildingBlocks.HealthCheck).
    public bool MetricsEnabled { get; set; } = true;
}
