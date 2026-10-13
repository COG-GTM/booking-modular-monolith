using BuildingBlocks.TestBase;
using Identity.Api;
using Identity.Data;
using Xunit;

namespace Identity.Api.Test;

[Collection(IdentityApiTestCollection.Name)]
public class IdentityApiTestBase : TestWriteBase<Program, IdentityContext>
{
    public IdentityApiTestBase(TestWriteFixture<Program, IdentityContext> integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class IdentityApiTestCollection : ICollectionFixture<TestWriteFixture<Program, IdentityContext>>
{
    public const string Name = "Identity Service Smoke Test";
}
