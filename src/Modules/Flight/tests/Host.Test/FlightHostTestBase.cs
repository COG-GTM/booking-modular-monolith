using BuildingBlocks.TestBase;
using Flight.Data;
using Xunit;

namespace Flight.Host.Test;

[Collection(FlightHostTestCollection.Name)]
public abstract class FlightHostTestBase : TestBase<global::Flight.Host.Program, FlightDbContext, FlightReadDbContext>
{
    protected FlightHostTestBase(
        TestFixture<global::Flight.Host.Program, FlightDbContext, FlightReadDbContext> fixture
    )
        : base(fixture) { }
}

[CollectionDefinition(Name)]
public sealed class FlightHostTestCollection
    : ICollectionFixture<TestFixture<global::Flight.Host.Program, FlightDbContext, FlightReadDbContext>>
{
    public const string Name = "Flight Host Integration Test";
}
