using BuildingBlocks.Web;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace BuildingBlocks.Unit.Test.WebTests;

public class ServiceProviderExtensionsTests
{
    private sealed class WebRecordingHostedService : IHostedService
    {
        public int Starts { get; private set; }
        public int Stops { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Starts++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            return Task.CompletedTask;
        }
    }

    private sealed class WebUnregisteredService : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task start_and_stop_test_hosted_services_lifecycle_registered_services_only()
    {
        var services = new ServiceCollection();
        var hosted = new WebRecordingHostedService();
        services.AddSingleton(hosted);
        var provider = services.BuildServiceProvider();

        var types = new[] { typeof(WebRecordingHostedService), typeof(WebUnregisteredService) };

        await provider.StartTestHostedServices(types);
        hosted.Starts.Should().Be(1);

        await provider.StopTestHostedServices(types);
        hosted.Stops.Should().Be(1);
    }
}
