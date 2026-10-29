using BuildingBlocks.TestBase;
using Flight.Data;
using Xunit;

namespace Integration.Test;

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture<Program, FlightDbContext, FlightReadDbContext>>
{
    public const string Name = "Flight Host Integration Test";
}

[Collection(IntegrationTestCollection.Name)]
public class FlightIntegrationTestBase : TestBase<Program, FlightDbContext, FlightReadDbContext>
{
    public FlightIntegrationTestBase(
        TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }
}
