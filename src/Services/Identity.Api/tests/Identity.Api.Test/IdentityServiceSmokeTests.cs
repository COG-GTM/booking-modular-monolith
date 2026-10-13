using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.Core;
using BuildingBlocks.TestBase;
using FluentAssertions;
using Identity.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Identity.Api.Test;

public class IdentityServiceSmokeTests : IdentityApiTestBase
{
    public IdentityServiceSmokeTests(TestWriteFixture<Program, IdentityContext> integrationTestFixture)
        : base(integrationTestFixture) { }

    [Fact]
    public async Task root_should_return_the_service_name()
    {
        var response = await Fixture.HttpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be("Identity-Service");
    }

    [Fact]
    public async Task openid_discovery_document_should_be_issued_by_the_identity_host()
    {
        var response = await Fixture.HttpClient.GetAsync("/.well-known/openid-configuration");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"token_endpoint\"");
    }

    [Fact]
    public async Task register_user_should_publish_user_created_through_the_service_outbox()
    {
        var request = new
        {
            FirstName = "Smoke",
            LastName = "Tester",
            Username = "smoke.tester",
            Email = "smoke.tester@test.com",
            Password = "Smoke@123456",
            ConfirmPassword = "Smoke@123456",
            PassportNumber = "SMK1234567",
        };

        var response = await Fixture.HttpClient.PostAsJsonAsync("/api/v1.0/identity/register-user", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await Fixture.WaitForPublishing<UserCreated>()).Should().BeTrue();
    }

    [Fact]
    public void host_should_only_wire_the_identity_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IEventMapper>().Should().BeOfType<IdentityEventMapper>();
    }
}
