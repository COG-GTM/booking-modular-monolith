using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gateway.Unit.Test;

public class GatewayHealthProbeTests
{
    [Theory]
    [InlineData("Development", "/health")]
    [InlineData("Development", "/alive")]
    [InlineData("Development", "/health/live")]
    [InlineData("Production", "/health")]
    [InlineData("Production", "/alive")]
    [InlineData("Production", "/health/live")]
    public async Task probes_are_mapped_once_by_service_defaults_in_every_environment(string environment, string path)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ObservabilityOptions:UsePrometheusExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseOTLPExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseAspireOTLPExporter", "false");
        });

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task readiness_probe_reports_each_check_as_json()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ObservabilityOptions:UsePrometheusExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseOTLPExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseAspireOTLPExporter", "false");
        });

        var response = await factory.CreateClient().GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Healthy\"", body, StringComparison.Ordinal);
        Assert.Contains("\"self\"", body, StringComparison.Ordinal);
    }
}
