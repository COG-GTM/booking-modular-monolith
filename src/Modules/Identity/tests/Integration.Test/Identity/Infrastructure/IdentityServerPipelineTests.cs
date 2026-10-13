using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using BuildingBlocks.EFCore;
using FluentAssertions;
using Identity.Data;
using Identity.Extensions.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WebMotions.Fake.Authentication.JwtBearer;
using Xunit;

namespace Integration.Test.Identity.Infrastructure;

/// <summary>
/// Exercises <see cref="InfrastructureExtensions.UseIdentityModules"/> inside a minimal pipeline that
/// mirrors Program.cs (UseAuthentication/UseAuthorization first, then the Identity module). The
/// default authenticate scheme is the ASP.NET Identity application cookie, so a request that carries
/// both a bearer token and another user's cookie must still execute handlers as the bearer identity.
/// </summary>
public class IdentityServerPipelineTests : IAsyncLifetime
{
    private const string ApiScopePolicy = "ApiScope";
    private const string IssuerUri = "https://identity.test";
    private const string ProtectedRoute = "/pipeline-tests/whoami";
    private const string AnonymousRoute = "/pipeline-tests/whoami-anonymous";

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["AuthOptions:IssuerUri"] = IssuerUri }
        );

        builder.Services.AddDbContext<IdentityContext>(options =>
            options.UseSqlite(_connection).ReplaceService<IMigrationsAssembly, NoMigrationsAssembly>()
        );
        builder.Services.AddScoped<ISeedManager, SeedManager>();
        builder.AddCustomIdentityServer();

        builder.Services.AddAuthentication().AddFakeJwtBearer();
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy(
                ApiScopePolicy,
                policy =>
                {
                    policy.AddAuthenticationSchemes(FakeJwtBearerDefaults.AuthenticationScheme);
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("scope", "flight-api");
                }
            );
        });

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.UseIdentityModules();

        _app.MapGet(ProtectedRoute, (ClaimsPrincipal user) => WhoAmI.From(user)).RequireAuthorization(ApiScopePolicy);
        _app.MapGet(AnonymousRoute, (ClaimsPrincipal user) => WhoAmI.From(user));

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task should_keep_bearer_identity_when_request_also_carries_another_users_identity_cookie()
    {
        // Arrange
        var bearerUserId = Guid.CreateVersion7();
        var cookieUserId = Guid.CreateVersion7();

        var request = new HttpRequestMessage(HttpMethod.Get, ProtectedRoute);
        AddFakeBearerToken(request, bearerUserId);
        AddIdentityCookie(request, cookieUserId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var whoAmI = await response.Content.ReadFromJsonAsync<WhoAmI>();
        whoAmI.Should().NotBeNull();
        whoAmI!.IsAuthenticated.Should().BeTrue();
        whoAmI.Sub.Should().Be(bearerUserId.ToString());
        whoAmI.Sub.Should().NotBe(cookieUserId.ToString());
        whoAmI.AuthenticationType.Should().NotBe(IdentityConstants.ApplicationScheme);
    }

    [Fact]
    public async Task should_authenticate_identity_cookie_through_default_scheme_on_anonymous_endpoint()
    {
        // Arrange: proves the cookie is valid and is the default scheme the duplicate middleware would re-run
        var cookieUserId = Guid.CreateVersion7();

        var request = new HttpRequestMessage(HttpMethod.Get, AnonymousRoute);
        AddIdentityCookie(request, cookieUserId);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var whoAmI = await response.Content.ReadFromJsonAsync<WhoAmI>();
        whoAmI.Should().NotBeNull();
        whoAmI!.IsAuthenticated.Should().BeTrue();
        whoAmI.Sub.Should().Be(cookieUserId.ToString());
        whoAmI.AuthenticationType.Should().Be(IdentityConstants.ApplicationScheme);
    }

    [Fact]
    public async Task should_reject_identity_cookie_alone_on_api_scope_protected_endpoint()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, ProtectedRoute);
        AddIdentityCookie(request, Guid.CreateVersion7());

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task should_still_serve_identity_server_discovery_endpoint()
    {
        // Act
        var response = await _client.GetAsync("/.well-known/openid-configuration");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var discovery = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        discovery.Should().NotBeNull();
        discovery!["issuer"].ToString().Should().Be(IssuerUri);
    }

    private static void AddFakeBearerToken(HttpRequestMessage request, Guid userId)
    {
        var claims = new Dictionary<string, object>
        {
            { "sub", userId.ToString() },
            { "name", "bearer-user" },
            { "scope", "flight-api" },
        };

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            FakeJwtBearerDefaults.AuthenticationScheme,
            System.Text.Json.JsonSerializer.Serialize(claims)
        );
    }

    private void AddIdentityCookie(HttpRequestMessage request, Guid userId)
    {
        var cookieOptions = _app
            .Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        var identity = new ClaimsIdentity(
            [new Claim("sub", userId.ToString()), new Claim("name", "cookie-user")],
            IdentityConstants.ApplicationScheme
        );

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IssuedUtc = DateTimeOffset.UtcNow },
            IdentityConstants.ApplicationScheme
        );

        var cookieValue = cookieOptions.TicketDataFormat.Protect(ticket);
        request.Headers.Add("Cookie", $"{cookieOptions.Cookie.Name}={cookieValue}");
    }

    private sealed record WhoAmI(bool IsAuthenticated, string? AuthenticationType, string? Sub)
    {
        public static WhoAmI From(ClaimsPrincipal user) =>
            new(
                user.Identity?.IsAuthenticated ?? false,
                user.Identity?.AuthenticationType,
                user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            );
    }

    private sealed class NoMigrationsAssembly : IMigrationsAssembly
    {
        public IReadOnlyDictionary<string, TypeInfo> Migrations { get; } = new Dictionary<string, TypeInfo>();

        public ModelSnapshot? ModelSnapshot => null;

        public Assembly Assembly => typeof(IdentityContext).Assembly;

        public string? FindMigrationId(string nameOrId) => null;

        public Migration CreateMigration(TypeInfo migrationClass, string activeProvider) =>
            throw new NotSupportedException();
    }
}
