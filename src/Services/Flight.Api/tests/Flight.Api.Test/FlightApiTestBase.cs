using BuildingBlocks.TestBase;
using Flight.Api;
using Flight.Data;
using Xunit;

namespace Flight.Api.Test;

[Collection(FlightApiTestCollection.Name)]
public class FlightApiTestBase : TestBase<Program, FlightDbContext, FlightReadDbContext>
{
    public FlightApiTestBase(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class FlightApiTestCollection : ICollectionFixture<TestFixture<Program, FlightDbContext, FlightReadDbContext>>
{
    public const string Name = "Flight Service Smoke Test";
}
