using BuildingBlocks.TestBase;
using Passenger.Data;
using Xunit;

namespace Passenger.Host.Test;

[Collection(HostIntegrationTestCollection.Name)]
public abstract class PassengerHostTestBase
    : TestBase<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext>
{
    protected PassengerHostTestBase(
        TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> fixture
    )
        : base(fixture) { }
}

[CollectionDefinition(Name)]
public sealed class HostIntegrationTestCollection
    : ICollectionFixture<TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "Passenger Host Integration Tests";
}
