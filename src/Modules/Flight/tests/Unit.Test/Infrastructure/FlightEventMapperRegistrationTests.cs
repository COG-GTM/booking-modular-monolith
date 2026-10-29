using BuildingBlocks.Core;
using Flight.Extensions.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Unit.Test.Infrastructure;

// The Api host no longer composes a CompositeEventMapper; each module must register its own mapper.
public class FlightEventMapperRegistrationTests
{
    [Fact]
    public void add_flight_modules_registers_the_flight_event_mapper_for_the_dispatcher()
    {
        var builder = WebApplication.CreateBuilder();

        builder.AddFlightModules();

        builder.Services.Where(x => x.ServiceType == typeof(IEventMapper)).Should().ContainSingle();

        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope
            .ServiceProvider.GetServices<IEventMapper>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<global::Flight.FlightEventMapper>();
    }
}
