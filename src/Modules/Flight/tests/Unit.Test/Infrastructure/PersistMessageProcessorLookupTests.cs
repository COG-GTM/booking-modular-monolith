using BuildingBlocks.PersistMessageProcessor;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Xunit;

namespace Unit.Test.Infrastructure;

using global::Flight;

// Module-agnostic infrastructure reaches a module's outbox/inbox store only through the module assembly key.
public class PersistMessageProcessorLookupTests
{
    public sealed class OtherModuleRoot;

    private static IServiceCollection RegisterTwoModules()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["PostgresOptions:ConnectionString:Flight"] = "Host=localhost;Database=flight_test",
                ["PostgresOptions:ConnectionString:Other"] = "Host=localhost;Database=other_test",
            }
        );

        builder.AddPersistMessageProcessor<FlightRoot>(nameof(Flight));
        builder.AddPersistMessageProcessor<OtherModuleRoot>("Other");

        return builder.Services;
    }

    [Fact]
    public void lookup_by_module_assembly_should_return_that_module_processor()
    {
        var services = RegisterTwoModules();

        // the typed processors need a live database, replace them with stand-ins
        var flightProcessor = Substitute.For<IPersistMessageProcessor<FlightRoot>>();
        var otherProcessor = Substitute.For<IPersistMessageProcessor<OtherModuleRoot>>();
        services.Replace(ServiceDescriptor.Scoped<IPersistMessageProcessor<FlightRoot>>(_ => flightProcessor));
        services.Replace(ServiceDescriptor.Scoped<IPersistMessageProcessor<OtherModuleRoot>>(_ => otherProcessor));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope
            .ServiceProvider.GetPersistMessageProcessor(typeof(FlightRoot).Assembly)
            .Should()
            .BeSameAs(flightProcessor);
        scope
            .ServiceProvider.GetPersistMessageProcessor(typeof(OtherModuleRoot).Assembly)
            .Should()
            .BeSameAs(otherProcessor);
    }

    [Fact]
    public void lookup_by_an_assembly_without_a_registered_module_should_throw()
    {
        var services = RegisterTwoModules();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetPersistMessageProcessor(typeof(string).Assembly);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void persist_message_options_should_be_registered_once_for_all_modules()
    {
        var services = RegisterTwoModules();

        services.Count(descriptor => descriptor.ServiceType == typeof(PersistMessageOptions)).Should().Be(1);
    }

    [Fact]
    public void each_module_store_should_be_a_distinct_db_context_type()
    {
        var services = RegisterTwoModules();

        services
            .Where(descriptor => descriptor.ServiceType.IsGenericType)
            .Where(descriptor =>
                descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IPersistMessageDbContext<>)
            )
            .Select(descriptor => descriptor.ServiceType)
            .Should()
            .BeEquivalentTo(
                [typeof(IPersistMessageDbContext<FlightRoot>), typeof(IPersistMessageDbContext<OtherModuleRoot>)]
            );
    }
}
