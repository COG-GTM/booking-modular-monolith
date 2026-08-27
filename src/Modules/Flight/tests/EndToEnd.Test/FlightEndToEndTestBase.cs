using BuildingBlocks.TestBase;
using Flight.Data;
using FlightService;
using Xunit;

namespace EndToEnd.Test;

public class FlightServiceTestFixture : TestFixture<Program, FlightDbContext, FlightReadDbContext>
{
    protected override TestInfrastructure RequiredInfrastructure =>
        TestInfrastructure.Postgres
        | TestInfrastructure.PersistMessagePostgres
        | TestInfrastructure.RabbitMq
        | TestInfrastructure.Mongo;
}

[Collection(EndToEndTestCollection.Name)]
public class FlightEndToEndTestBase : TestBase<Program, FlightDbContext, FlightReadDbContext>
{
    public FlightEndToEndTestBase(FlightServiceTestFixture integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class EndToEndTestCollection : ICollectionFixture<FlightServiceTestFixture>
{
    public const string Name = "Flight EndToEnd Test";
}
