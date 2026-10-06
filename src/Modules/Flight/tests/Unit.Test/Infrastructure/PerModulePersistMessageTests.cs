using BuildingBlocks.Core;
using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;

public class PerModulePersistMessageTests
{
    private const string FlightConnectionString = "Host=localhost;Database=flight_test;Username=flight;Password=flight";
    private const string OtherConnectionString = "Host=localhost;Database=other_test;Username=other;Password=other";

    private sealed class OtherModuleRoot;

    private static IServiceCollection RegisterTwoModules()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = FlightConnectionString,
                ["PostgresOptions:ConnectionString:Other"] = OtherConnectionString,
            }
        );

        builder.AddPersistMessageProcessor<FlightRoot>(nameof(Flight));
        builder.AddPersistMessageProcessor<OtherModuleRoot>("Other");

        return builder.Services;
    }

    [Fact]
    public void each_module_outbox_should_use_its_own_connection_string()
    {
        var services = RegisterTwoModules();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var flightStore = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<FlightRoot>>();
        var otherStore = scope.ServiceProvider.GetRequiredService<PersistMessageDbContext<OtherModuleRoot>>();

        flightStore.Database.GetConnectionString().Should().Be(FlightConnectionString);
        otherStore.Database.GetConnectionString().Should().Be(OtherConnectionString);
    }

    [Fact]
    public void each_module_should_get_its_own_processor_dispatcher_and_background_service()
    {
        var services = RegisterTwoModules();

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .Should()
            .BeEquivalentTo(
                [
                    typeof(PersistMessageBackgroundService<FlightRoot>),
                    typeof(PersistMessageBackgroundService<OtherModuleRoot>),
                ]
            );

        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IPersistMessageProcessor<FlightRoot>));
        services
            .Should()
            .Contain(descriptor => descriptor.ServiceType == typeof(IPersistMessageProcessor<OtherModuleRoot>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IEventDispatcher<FlightRoot>));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IEventDispatcher<OtherModuleRoot>));

        // no module-agnostic (shared) store is registered any more
        services
            .Should()
            .NotContain(descriptor =>
                descriptor.ServiceType == typeof(IPersistMessageProcessor) && !descriptor.IsKeyedService
            );
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IPersistMessageDbContext));
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IEventDispatcher));
    }

    [Fact]
    public void processor_should_be_resolvable_by_the_owning_module_assembly()
    {
        var services = RegisterTwoModules();

        services
            .Should()
            .Contain(descriptor =>
                descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(IPersistMessageProcessor)
                && Equals(descriptor.ServiceKey, typeof(FlightRoot).Assembly)
            );
    }
}
