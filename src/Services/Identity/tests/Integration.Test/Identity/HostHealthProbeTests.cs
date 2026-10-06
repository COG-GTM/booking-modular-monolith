using System.Net;
using FluentAssertions;
using Identity.Host.Integration.Test;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Identity.Host.Integration.Test.Identity;

public class HostHealthProbeTests(
    BuildingBlocks.TestBase.TestWriteFixture<
        global::Identity.Host.Program,
        global::Identity.Data.IdentityContext
    > integrationTestFactory
) : IdentityHostIntegrationTestBase(integrationTestFactory)
{
    [Fact]
    public async Task health_endpoint_should_be_mapped_outside_development()
    {
        Fixture.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment().Should().BeFalse();
        using var httpClient = Fixture.HttpClient;

        using var response = await httpClient.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }

    [Fact]
    public async Task alive_endpoint_should_be_mapped_outside_development()
    {
        Fixture.ServiceProvider.GetRequiredService<IHostEnvironment>().IsDevelopment().Should().BeFalse();
        using var httpClient = Fixture.HttpClient;

        using var response = await httpClient.GetAsync("/alive");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
