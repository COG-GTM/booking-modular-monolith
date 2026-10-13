using BuildingBlocks.TestBase;
using Passenger.Api;
using Passenger.Data;
using Xunit;

namespace Passenger.Api.Test;

[Collection(PassengerApiTestCollection.Name)]
public class PassengerApiTestBase : TestBase<Program, PassengerDbContext, PassengerReadDbContext>
{
    public PassengerApiTestBase(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class PassengerApiTestCollection
    : ICollectionFixture<TestFixture<Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "Passenger Service Smoke Test";
}
