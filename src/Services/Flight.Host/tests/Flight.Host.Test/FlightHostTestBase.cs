using BuildingBlocks.TestBase;
using Flight.Data;
using Flight.Host;
using Xunit;

namespace Flight.Host.Test;

[Collection(FlightHostTestCollection.Name)]
public class FlightHostTestBase : TestBase<Program, FlightDbContext, FlightReadDbContext>
{
    public FlightHostTestBase(TestFixture<Program, FlightDbContext, FlightReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class FlightHostTestCollection : ICollectionFixture<TestFixture<Program, FlightDbContext, FlightReadDbContext>>
{
    public const string Name = "Flight Host Test";
}
