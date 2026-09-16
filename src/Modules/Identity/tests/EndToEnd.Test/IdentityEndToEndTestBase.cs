using Api;
using BuildingBlocks.TestBase;
using Identity.Data;
using Xunit;

namespace EndToEnd.Test;

[Collection(EndToEndTestCollection.Name)]
public class IdentityEndToEndTestBase : TestWriteBase<Program, IdentityContext>
{
    public IdentityEndToEndTestBase(TestWriteFixture<Program, IdentityContext> integrationTestFixture)
        : base(integrationTestFixture) { }
}

[CollectionDefinition(Name)]
public class EndToEndTestCollection : ICollectionFixture<TestWriteFixture<Program, IdentityContext>>
{
    public const string Name = "Identity EndToEnd Test";
}
