using BuildingBlocks.TestBase;
using Identity.Data;
using Xunit;

namespace Host.Test;

[Collection(IdentityHostTestCollection.Name)]
public class IdentityHostTestBase : TestWriteBase<Identity.Host.Program, IdentityContext>
{
    public IdentityHostTestBase(TestWriteFixture<Identity.Host.Program, IdentityContext> fixture)
        : base(fixture) { }
}

[CollectionDefinition(Name)]
public class IdentityHostTestCollection : ICollectionFixture<TestWriteFixture<Identity.Host.Program, IdentityContext>>
{
    public const string Name = "Identity Host Integration Test";
}
