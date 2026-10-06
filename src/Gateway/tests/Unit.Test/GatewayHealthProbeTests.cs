using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Gateway.Unit.Test;

public class GatewayHealthProbeTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task in_development_probes_are_mapped_once_by_service_defaults(string path)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ObservabilityOptions:UsePrometheusExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseOTLPExporter", "false");
            builder.UseSetting("ObservabilityOptions:UseAspireOTLPExporter", "false");
        });

        var response = await factory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
