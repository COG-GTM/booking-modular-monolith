using BuildingBlocks.TestBase;
using Identity.Data;
using Xunit;

namespace Identity.Host.Test;

[Collection(IdentityHostCollection.Name)]
public class IdentityHostTestBase : TestWriteBase<Program, IdentityContext>
{
    public IdentityHostTestBase(TestWriteFixture<Program, IdentityContext> fixture)
        : base(fixture) { }
}

[CollectionDefinition(Name)]
public class IdentityHostCollection : ICollectionFixture<TestWriteFixture<Program, IdentityContext>>
{
    public const string Name = "Identity Host Test";
}
