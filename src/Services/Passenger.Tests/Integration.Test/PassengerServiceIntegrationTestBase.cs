using BuildingBlocks.TestBase;
using Passenger.Data;
using PassengerService;
using Xunit;

namespace Integration.Test;

[Collection(IntegrationTestCollection.Name)]
public class PassengerServiceIntegrationTestBase : TestBase<Program, PassengerDbContext, PassengerReadDbContext>
{
    public PassengerServiceIntegrationTestBase(
        TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory
    )
        : base(integrationTestFactory) { }
}

[CollectionDefinition(Name)]
public class IntegrationTestCollection
    : ICollectionFixture<TestFixture<Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "PassengerService Integration Test";
}
