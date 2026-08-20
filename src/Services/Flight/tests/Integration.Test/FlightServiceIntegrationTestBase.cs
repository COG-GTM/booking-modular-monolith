using BuildingBlocks.TestBase;
using Flight.Data;
using Xunit;

namespace FlightService.Integration.Test;

[Collection(FlightServiceIntegrationTestCollection.Name)]
public class FlightServiceIntegrationTestBase : TestBase<Program, FlightDbContext, FlightReadDbContext>
{
    public FlightServiceIntegrationTestBase(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture) : base(integrationTestFixture)
    {
    }
}

[CollectionDefinition(Name)]
public class FlightServiceIntegrationTestCollection : ICollectionFixture<TestFixture<Program, FlightDbContext, FlightReadDbContext>>
{
    public const string Name = "FlightService Integration Test";
}
