using BuildingBlocks.TestBase;
using Identity.Data;
using IdentityService;
using Xunit;

namespace Integration.Test;

public class IdentityServiceTestFixture : TestWriteFixture<Program, IdentityContext>
{
    protected override TestInfrastructure RequiredInfrastructure =>
        TestInfrastructure.Postgres | TestInfrastructure.PersistMessagePostgres | TestInfrastructure.RabbitMq;
}

[Collection(IntegrationTestCollection.Name)]
public class IdentityIntegrationTestBase : TestWriteBase<Program, IdentityContext>
{
    public IdentityIntegrationTestBase(IdentityServiceTestFixture integrationTestFactory)
        : base(integrationTestFactory) { }
}

[CollectionDefinition(Name)]
public class IntegrationTestCollection : ICollectionFixture<IdentityServiceTestFixture>
{
    public const string Name = "Identity Integration Test";
}
