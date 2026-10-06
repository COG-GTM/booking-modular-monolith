using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;
using FluentAssertions;
using Identity.Host.Integration.Test;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IdentityEventMapper = global::Identity.IdentityEventMapper;
using IdentityRoot = global::Identity.IdentityRoot;
using RegisterNewUserRequestDto = global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUserRequestDto;
using RegisterNewUserResponseDto = global::Identity.Identity.Features.RegisteringNewUser.V1.RegisterNewUserResponseDto;

namespace Identity.Host.Integration.Test.Identity;

public class HostInfrastructureTests(
    BuildingBlocks.TestBase.TestWriteFixture<
        global::Identity.Host.Program,
        global::Identity.Data.IdentityContext
    > integrationTestFactory
) : IdentityHostIntegrationTestBase(integrationTestFactory)
{
    private const string RegisterUserPath = "/api/v1/identity/register-user";

    [Fact]
    public void host_should_resolve_identity_event_mapper_as_the_only_event_mapper()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        var eventMapper = scope.ServiceProvider.GetRequiredService<IEventMapper>();

        eventMapper.Should().BeOfType<IdentityEventMapper>();
        scope
            .ServiceProvider.GetServices<IEventMapper>()
            .Should()
            .AllSatisfy(mapper => mapper.Should().BeOfType<IdentityEventMapper>());
    }

    [Fact]
    public void host_should_use_http_context_backed_event_headers_and_current_user_providers()
    {
        using var scope = Fixture.ServiceProvider.CreateScope();

        scope
            .ServiceProvider.GetRequiredService<IEventHeadersProvider>()
            .Should()
            .BeOfType<HttpContextEventHeadersProvider>();
        scope.ServiceProvider.GetRequiredService<ICurrentUserProvider>().Should().BeOfType<CurrentUserProvider>();
    }

    [Fact]
    public void host_should_run_only_the_identity_outbox_processor()
    {
        Fixture
            .PersistMessageBackgroundServiceTypes.Should()
            .ContainSingle()
            .Which.Should()
            .Be(typeof(PersistMessageBackgroundService<IdentityRoot>));

        Fixture.ServiceProvider.GetRequiredService<IPersistMessageDbContext<IdentityRoot>>().Should().NotBeNull();
    }

    [Fact]
    public void host_should_not_register_a_mongo_read_store()
    {
        Fixture.ServiceProvider.GetService<IMongoDbContext>().Should().BeNull();
    }

    [Fact]
    public void host_should_validate_tokens_against_its_own_issuer()
    {
        var issuer = Fixture.Configuration["AuthOptions:IssuerUri"];

        issuer.Should().NotBeNullOrWhiteSpace();
        Fixture.Configuration["Jwt:Authority"].Should().Be(issuer);
        Fixture.Configuration["MessageBroker:ServiceName"].Should().Be("identity");
        Fixture.Configuration["PostgresOptions:ConnectionString:Identity"].Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task root_endpoint_should_return_service_name()
    {
        using var httpClient = Fixture.HttpClient;

        using var response = await httpClient.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Be(Fixture.Configuration["AppOptions:Name"]);
    }

    [Fact]
    public async Task openid_configuration_should_advertise_configured_issuer()
    {
        using var httpClient = Fixture.HttpClient;

        using var response = await httpClient.GetAsync("/.well-known/openid-configuration");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var discovery = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        discovery
            .RootElement.GetProperty("issuer")
            .GetString()
            .Should()
            .Be(Fixture.Configuration["AuthOptions:IssuerUri"]);
        discovery
            .RootElement.GetProperty("scopes_supported")
            .EnumerateArray()
            .Select(x => x.GetString())
            .Should()
            .Contain("booking-modular-monolith");
    }

    [Fact]
    public async Task register_user_endpoint_should_register_user_over_http()
    {
        await EnsureUserRoleExistsAsync();
        var request = new RegisterNewUserRequestDto(
            "Http",
            "User",
            "identity-host-http-user",
            "identity-host-http-user@test.com",
            "Password@123",
            "Password@123",
            "11223344"
        );
        using var httpClient = Fixture.HttpClient;

        using var response = await httpClient.PostAsJsonAsync(RegisterUserPath, request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RegisterNewUserResponseDto>();
        body.Should().NotBeNull();
        body!.Username.Should().Be(request.Username);
        body.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task register_user_endpoint_should_reject_anonymous_requests()
    {
        var request = new RegisterNewUserRequestDto(
            "Anon",
            "User",
            "identity-host-anonymous-user",
            "identity-host-anonymous-user@test.com",
            "Password@123",
            "Password@123",
            "55667788"
        );
        using var httpClient = Fixture.HttpClient;
        httpClient.DefaultRequestHeaders.Authorization = null;

        using var response = await httpClient.PostAsJsonAsync(RegisterUserPath, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task register_user_endpoint_should_return_problem_details_when_passwords_do_not_match()
    {
        await EnsureUserRoleExistsAsync();
        var request = new RegisterNewUserRequestDto(
            "Mismatch",
            "User",
            "identity-host-mismatch-user",
            "identity-host-mismatch-user@test.com",
            "Password@123",
            "Different@123",
            "99887766"
        );
        using var httpClient = Fixture.HttpClient;

        using var response = await httpClient.PostAsJsonAsync(RegisterUserPath, request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }
}
