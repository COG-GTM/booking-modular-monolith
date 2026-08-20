using BuildingBlocks.TestBase;
using Identity.Data;
using IdentityService;
using Xunit;

namespace Integration.Test;

[Collection(IdentityServiceIntegrationTestCollection.Name)]
public class IdentityServiceIntegrationTestBase : TestWriteBase<Program, IdentityContext>
{
    public IdentityServiceIntegrationTestBase(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }
}

[CollectionDefinition(Name)]
public class IdentityServiceIntegrationTestCollection : ICollectionFixture<TestWriteFixture<Program, IdentityContext>>
{
    public const string Name = "Identity Service Integration Test";
}
