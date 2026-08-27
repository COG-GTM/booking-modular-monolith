using BuildingBlocks.TestBase;
using Flight.Data;
using FlightService;
using Xunit;

namespace Integration.Test;

public class FlightServiceTestFixture : TestFixture<Program, FlightDbContext, FlightReadDbContext>
{
    protected override TestInfrastructure RequiredInfrastructure =>
        TestInfrastructure.Postgres
        | TestInfrastructure.PersistMessagePostgres
        | TestInfrastructure.RabbitMq
        | TestInfrastructure.Mongo;
}

[Collection(IntegrationTestCollection.Name)]
public class FlightIntegrationTestBase : TestBase<Program, FlightDbContext, FlightReadDbContext>
{
    public FlightIntegrationTestBase(FlightServiceTestFixture integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<FlightServiceTestFixture>
{
    public const string Name = "Flight Integration Test";
}
