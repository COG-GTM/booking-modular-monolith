using System.Net;
using System.Text.Json;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using EasyCaching.Core;
using FluentAssertions;
using FluentValidation;
using Identity.Extensions.Infrastructure;
using Identity.Host.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using TransportType = BuildingBlocks.MassTransit.TransportType;
using ValidationException = BuildingBlocks.Exception.ValidationException;

namespace Identity.Host.Test;

// Docker-free pipeline checks: TestServer + InMemory transport, same composition as Program.cs.
public class IdentityHostInfrastructureTests
{
    private static WebApplicationBuilder CreateBuilder(params KeyValuePair<string, string?>[] overrides)
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "test",
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = typeof(Program).Assembly.GetName().Name,
            }
        );
        builder.Configuration.AddInMemoryCollection(
            new[]
            {
                new KeyValuePair<string, string?>("ObservabilityOptions:UseOTLPExporter", "false"),
                new KeyValuePair<string, string?>("ObservabilityOptions:UseAspireOTLPExporter", "false"),
                new KeyValuePair<string, string?>("ObservabilityOptions:UsePrometheusExporter", "false"),
                new KeyValuePair<string, string?>("MessageBroker:TransportType", nameof(TransportType.InMemory)),
                new KeyValuePair<string, string?>("MessageBroker:UseInbox", "false"),
                new KeyValuePair<string, string?>("HealthOptions:DependencyChecksEnabled", "false"),
            }.Concat(overrides)
        );

        builder.WebHost.UseTestServer();
        builder.AddIdentityServiceInfrastructure();

        return builder;
    }

    private static WebApplication BuildApp(WebApplicationBuilder builder)
    {
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseIdentityServiceInfrastructure();
        return app;
    }

    [Fact]
    public async Task problem_details_should_map_app_and_validation_exceptions_to_400_and_others_to_500()
    {
        var builder = CreateBuilder();
        var app = BuildApp(builder);

        app.MapGet(
            "/throw-app",
            (HttpContext _) => throw new global::Identity.Identity.Exceptions.RegisterIdentityUserException("boom")
        );
        app.MapGet("/throw-validation", (HttpContext _) => throw new ValidationException("invalid"));
        app.MapGet("/throw-other", (HttpContext _) => throw new InvalidOperationException("unexpected"));

        await app.StartAsync();

        try
        {
            using var client = app.GetTestClient();

            var appResponse = await client.GetAsync("/throw-app");
            appResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            appResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
            var body = await appResponse.Content.ReadAsStringAsync();
            body.Should().Contain("boom");

            (await client.GetAsync("/throw-validation")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await client.GetAsync("/throw-other")).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task api_scope_policy_endpoint_should_return_401_without_token()
    {
        var builder = CreateBuilder();
        var app = BuildApp(builder);

        app.MapGet("/scoped", (HttpContext _) => Task.FromResult("ok"))
            .RequireAuthorization(BuildingBlocks.Constants.IdentityConstant.Policy.ApiScope);

        await app.StartAsync();

        try
        {
            using var client = app.GetTestClient();

            (await client.GetAsync("/scoped")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task readiness_probe_should_be_mapped()
    {
        var builder = CreateBuilder();
        var app = BuildApp(builder);
        await app.StartAsync();

        try
        {
            using var client = app.GetTestClient();

            (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    [Fact]
    public void infrastructure_should_register_current_user_provider_easycaching_and_event_headers()
    {
        var builder = CreateBuilder();

        builder
            .Services.Should()
            .Contain(d =>
                d.ServiceType == typeof(ICurrentUserProvider) && d.ImplementationType == typeof(CurrentUserProvider)
            );
        builder.Services.Should().Contain(d => d.ServiceType == typeof(IEventHeadersProvider));

        using var provider = builder.Services.BuildServiceProvider();
        provider.GetRequiredService<IEasyCachingProviderFactory>().GetCachingProvider("mem").Should().NotBeNull();
    }

    [Fact]
    public void service_provider_should_build_with_validation_when_module_is_composed()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions
            {
                EnvironmentName = "test",
                ContentRootPath = AppContext.BaseDirectory,
                ApplicationName = typeof(Program).Assembly.GetName().Name,
            }
        );
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ObservabilityOptions:UseOTLPExporter"] = "false",
                ["ObservabilityOptions:UseAspireOTLPExporter"] = "false",
                ["ObservabilityOptions:UsePrometheusExporter"] = "false",
                ["MessageBroker:TransportType"] = nameof(TransportType.InMemory),
                ["MessageBroker:UseInbox"] = "false",
                ["HealthOptions:DependencyChecksEnabled"] = "false",
            }
        );

        builder.AddIdentityServiceInfrastructure();
        builder.AddIdentityModules();

        var act = () =>
            builder.Services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
            );

        act.Should().NotThrow();
    }
}
