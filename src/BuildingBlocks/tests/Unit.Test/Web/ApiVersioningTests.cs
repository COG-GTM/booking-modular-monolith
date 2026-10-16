using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class ApiVersioningTests
{
    [Fact]
    public void add_custom_versioning_configures_reporting_default_version_and_explorer()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCustomVersioning(o => o.ReportApiVersions = false);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ApiVersioningOptions>>().Value;
        options.ReportApiVersions.Should().BeFalse();
        options.AssumeDefaultVersionWhenUnspecified.Should().BeTrue();
        options.DefaultApiVersion.Should().Be(new ApiVersion(1, 0));
        options.ApiVersionReader.Should().NotBeNull();

        var explorer = provider.GetRequiredService<IOptions<ApiExplorerOptions>>().Value;
        explorer.GroupNameFormat.Should().Be("'v'VVV");
        explorer.SubstituteApiVersionInUrl.Should().BeTrue();
    }

    [Fact]
    public void add_custom_versioning_defaults_report_api_versions_to_true()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCustomVersioning();

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<ApiVersioningOptions>>().Value.ReportApiVersions.Should().BeTrue();
    }

    private static async Task<(WebApplication App, HttpClient Client)> StartVersionedApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Services.AddCustomVersioning();
        builder.Services.AddEndpointsApiExplorer();
        var app = builder.Build();
        var versionSet = app.NewApiVersionSet().HasApiVersion(new ApiVersion(1, 0)).ReportApiVersions().Build();
        app.MapGet("api/v{version:apiVersion}/bb-versioned", () => "v1").WithApiVersionSet(versionSet);
        app.MapGet("bb-header-versioned", () => "v1").WithApiVersionSet(versionSet);
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    [Fact]
    public async Task url_segment_versioning_serves_the_endpoint_and_reports_versions()
    {
        var (app, client) = await StartVersionedApp();
        await using (app)
        {
            var response = await client.GetAsync("/api/v1/bb-versioned");

            response.EnsureSuccessStatusCode();
            (await response.Content.ReadAsStringAsync()).Should().Be("v1");
            response.Headers.Should().ContainKey("api-supported-versions");
        }
    }

    [Fact]
    public async Task header_versioning_serves_an_unsegmented_route_via_the_api_version_header()
    {
        var (app, client) = await StartVersionedApp();
        await using (app)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/bb-header-versioned");
            request.Headers.TryAddWithoutValidation("api-version", "1.0");

            var response = await client.SendAsync(request);

            response.EnsureSuccessStatusCode();
            (await response.Content.ReadAsStringAsync()).Should().Be("v1");
        }
    }
}
