using BuildingBlocks.TestBase;
using Passenger.Data;
using Xunit;

namespace Host.Test;

[Collection(PassengerHostTestCollection.Name)]
public class PassengerHostTestBase
    : TestBase<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext>
{
    public PassengerHostTestBase(
        TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }
}

[CollectionDefinition(Name)]
public class PassengerHostTestCollection
    : ICollectionFixture<TestFixture<global::Passenger.Host.Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "Passenger Host Test";
}
