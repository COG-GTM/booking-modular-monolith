using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BuildingBlocks.Web;

public static class TestServiceCollectionExtensions
{
    public static IServiceCollection ReplaceServiceWithSingletonMock<TService>(this IServiceCollection services)
        where TService : class
    {
        services.Unregister<TService>();
        services.AddSingleton(_ => Substitute.For<TService>());
        return services;
    }
}
