using BuildingBlocks.TestBase;
using Passenger.Data;
using PassengerService;
using Xunit;

namespace Integration.Test;

public class PassengerServiceTestFixture : TestFixture<Program, PassengerDbContext, PassengerReadDbContext>
{
    protected override TestInfrastructure RequiredInfrastructure =>
        TestInfrastructure.Postgres
        | TestInfrastructure.PersistMessagePostgres
        | TestInfrastructure.RabbitMq
        | TestInfrastructure.Mongo;
}

[Collection(IntegrationTestCollection.Name)]
public class PassengerIntegrationTestBase : TestBase<Program, PassengerDbContext, PassengerReadDbContext>
{
    public PassengerIntegrationTestBase(PassengerServiceTestFixture integrationTestFactory)
        : base(integrationTestFactory) { }
}

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<PassengerServiceTestFixture>
{
    public const string Name = "Passenger Integration Test";
}
