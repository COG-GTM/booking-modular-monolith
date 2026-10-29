using System.Net;
using BuildingBlocks.HealthCheck;
using FluentAssertions;
using HealthChecks.UI.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace BuildingBlocks.Tests;

public class HealthCheckUiOptionsTests
{
    [Fact]
    public async Task ui_opt_out_leaves_dashboard_unmapped_when_dependency_checks_are_enabled()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["HealthOptions:Enabled"] = "true",
                ["HealthOptions:UIEnabled"] = "false",
                ["AppOptions:Name"] = "Identity-Service",
            }
        );
        builder.Services.AddCustomHealthCheck();
        await using var app = builder.Build();
        app.UseCustomHealthCheck();
        await app.StartAsync();
        using var client = app.GetTestClient();

        using var response = await client.GetAsync("/health-ui");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ui_opt_out_preserves_dependency_checks_without_registering_ui_storage(
        bool? uiEnabled,
        bool expectUi
    )
    {
        await using var provider = CreateProvider(true, uiEnabled);
        using var scope = provider.CreateScope();

        var checks = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        checks.Select(check => check.Name).Should().BeEquivalentTo(["self", "rabbitmq", "Postgres-Identity"]);
        var self = checks.Should().ContainSingle(check => check.Name == "self").Subject;
        self.Tags.Should().Contain("live");
        var result = await self.Factory(provider).CheckHealthAsync(new HealthCheckContext { Registration = self });
        result.Status.Should().Be(HealthStatus.Healthy);

        var storage = scope.ServiceProvider.GetService<HealthChecksDb>();
        if (expectUi)
        {
            storage.Should().NotBeNull();
            InMemoryDatabaseFacadeExtensions.IsInMemory(storage!.Database).Should().BeTrue();
        }
        else
        {
            storage.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task disabled_health_checks_omit_ui_storage_regardless_of_ui_setting(bool? uiEnabled)
    {
        await using var provider = CreateProvider(false, uiEnabled);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<HealthChecksDb>().Should().BeNull();
        var report = await provider.GetRequiredService<HealthCheckService>().CheckHealthAsync();
        report.Status.Should().Be(HealthStatus.Healthy);
        report.Entries.Keys.Should().Equal("self");
    }

    private static ServiceProvider CreateProvider(bool enabled, bool? uiEnabled)
    {
        var settings = new Dictionary<string, string?>
        {
            ["HealthOptions:Enabled"] = enabled.ToString(),
            ["AppOptions:Name"] = "Identity-Service",
            ["PostgresOptions:ConnectionString:Identity"] = "Host=localhost;Database=identity_service",
        };
        if (uiEnabled.HasValue)
            settings["HealthOptions:UIEnabled"] = uiEnabled.Value.ToString();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddCustomHealthCheck();
        return services.BuildServiceProvider();
    }
}
