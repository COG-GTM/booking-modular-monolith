using Api;
using BuildingBlocks.TestBase;
using Integration.Test.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Xunit;

namespace Integration.Test;

[Collection(IntegrationTestCollection.Name)]
public class PassengerIntegrationTestBase : TestBase<Program, PassengerDbContext, PassengerReadDbContext>
{
    public PassengerIntegrationTestBase(TestFixture<Program, PassengerDbContext, PassengerReadDbContext> integrationTestFactory)
        : base(integrationTestFactory)
    {
    }

    protected override void RegisterTestsServices(IServiceCollection services)
    {
        FakeCurrentUserProvider.Register(services);
    }
}

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<TestFixture<Program, PassengerDbContext, PassengerReadDbContext>>
{
    public const string Name = "Passenger Integration Test";
}