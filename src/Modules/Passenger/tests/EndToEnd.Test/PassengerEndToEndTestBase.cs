using Api;
using BuildingBlocks.TestBase;
using Passenger.Data;
using Xunit;

namespace EndToEnd.Test;

[Collection(EndToEndTestCollection.Name)]
public class PassengerEndToEndTestBase : TestBase<Program, PassengerDbContext, PassengerReadDbContext>
{
    public PassengerEndToEndTestBase(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFixture
    )
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class EndToEndTestCollection
    : ICollectionFixture<TestFixture<Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "Passenger EndToEnd Test";
}
