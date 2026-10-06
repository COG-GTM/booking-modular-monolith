using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Unit.Test.PersistMessageProcessor;

public class ModuleBackgroundProcessingRegistrationTests
{
    public sealed class TestModule { }

    [Fact]
    public void adds_module_worker_when_configuration_is_missing()
    {
        var builder = CreateBuilder();

        builder.Services.Should().Contain(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(PersistMessageBackgroundService<TestModule>)
        );
    }

    [Fact]
    public void omits_module_worker_when_disabled_but_keeps_processor_and_dispatcher()
    {
        var builder = CreateBuilder(backgroundProcessingEnabled: false);

        builder.Services.Should().NotContain(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(PersistMessageBackgroundService<TestModule>)
        );
        builder.Services.Should().Contain(d => d.ServiceType == typeof(IPersistMessageProcessor<TestModule>));
        builder.Services.Should().Contain(d => d.ServiceType == typeof(IEventDispatcher<TestModule>));
    }

    private static WebApplicationBuilder CreateBuilder(bool backgroundProcessingEnabled = true)
    {
        var builder = WebApplication.CreateBuilder();
        if (!backgroundProcessingEnabled)
        {
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Modules:Test:BackgroundProcessingEnabled"] = "false" }
            );
        }

        builder.AddPersistMessageProcessor<TestModule>("Test");
        return builder;
    }
}
