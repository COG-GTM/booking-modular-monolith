using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class MinimalApiExtensionsTests
{
    public sealed class MinimalPingEndpoint : IMinimalEndpoint
    {
        public IEndpointRouteBuilder MapEndpoint(IEndpointRouteBuilder builder)
        {
            builder.MapGet("/bb-tests/ping", () => "pong");
            return builder;
        }
    }

    [Fact]
    public async Task add_and_map_minimal_endpoints_registers_and_serves_the_endpoint()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.AddMinimalEndpoints(assemblies: typeof(MinimalPingEndpoint).Assembly);
        var app = builder.Build();
        app.MapMinimalEndpoints();
        await app.StartAsync();
        await using (app)
        {
            var client = app.GetTestClient();

            var response = await client.GetAsync("/bb-tests/ping");

            response.EnsureSuccessStatusCode();
            (await response.Content.ReadAsStringAsync()).Should().Be("pong");
        }
    }

    [Fact]
    public void add_minimal_endpoints_registers_endpoints_as_iminimalendpoint_with_the_given_lifetime()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddMinimalEndpoints(lifetime: ServiceLifetime.Scoped, assemblies: typeof(MinimalPingEndpoint).Assembly);

        var descriptor = builder
            .Services.Should()
            .ContainSingle(d =>
                d.ServiceType == typeof(IMinimalEndpoint) && d.ImplementationType == typeof(MinimalPingEndpoint)
            )
            .Which;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }
}
