using global::Identity;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Identity.Host.Integration.Test;
using Xunit;

namespace Identity.Host.Integration.Test.Identity;

public class HostCompositionTests(
    BuildingBlocks.TestBase.TestWriteFixture<
        global::Identity.Host.Program,
        global::Identity.Data.IdentityContext
    > integrationTestFactory
) : IdentityHostIntegrationTestBase(integrationTestFactory)
{
    [Fact]
    public void host_should_not_compose_other_modules()
    {
        Fixture
            .PersistMessageDbContextTypes.Should()
            .ContainSingle()
            .Which.Should()
            .Be(typeof(IPersistMessageDbContext<IdentityRoot>));
    }
}
