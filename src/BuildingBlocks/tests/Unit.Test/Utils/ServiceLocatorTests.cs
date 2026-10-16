using BuildingBlocks.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildingBlocks.Unit.Test.UtilsTests;

[CollectionDefinition("Utils static state", DisableParallelization = true)]
public class UtilsStaticStateCollection;

[Collection("Utils static state")]
public class ServiceLocatorTests
{
    private sealed class UtilsLocatedService;

    [Fact]
    public void get_instance_resolves_registered_services_by_type_and_generic()
    {
        var services = new ServiceCollection().AddSingleton<UtilsLocatedService>().BuildServiceProvider();
        var expected = services.GetRequiredService<UtilsLocatedService>();
        ServiceLocator.SetLocatorProvider(services);

        ServiceLocator.Current.GetInstance<UtilsLocatedService>().Should().BeSameAs(expected);
        ServiceLocator.Current.GetInstance(typeof(UtilsLocatedService)).Should().BeSameAs(expected);
        ServiceLocator.Current.GetInstance(typeof(string)).Should().BeNull();
    }
}
