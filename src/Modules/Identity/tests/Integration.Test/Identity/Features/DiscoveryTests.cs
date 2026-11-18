using System.Net;
using System.Threading.Tasks;
using Api;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Xunit;

namespace Integration.Test.Identity.Features;

public class DiscoveryTests : IdentityIntegrationTestBase
{
    public DiscoveryTests(TestWriteFixture<Program, IdentityContext> integrationTestFactory)
        : base(integrationTestFactory) { }

    [Fact]
    public async Task should_allow_anonymous_access_to_openid_discovery()
    {
        using var anonymousHttpClient = Fixture.AnonymousHttpClient;
        var response = await anonymousHttpClient.GetAsync("/.well-known/openid-configuration");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
