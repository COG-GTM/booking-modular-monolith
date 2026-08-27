namespace BuildingBlocks.TestBase;

/// <summary>
/// Backing infrastructure a service host needs while under test.
/// </summary>
[Flags]
public enum TestInfrastructure
{
    None = 0,
    Postgres = 1 << 0,
    PersistMessagePostgres = 1 << 1,
    RabbitMq = 1 << 2,
    Mongo = 1 << 3,
    EventStore = 1 << 4,
    All = Postgres | PersistMessagePostgres | RabbitMq | Mongo | EventStore,
}
