namespace BuildingBlocks.HealthCheck;

public class HealthOptions
{
    // Turns on the HealthChecks UI (/health-ui). Probe endpoints and dependency checks are always on.
    public bool Enabled { get; set; }

    // Registers the readiness checks for the Postgres/Mongo/EventStoreDB/RabbitMQ dependencies found in configuration.
    public bool DependencyChecksEnabled { get; set; } = true;
}
