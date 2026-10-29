using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace BuildingBlocks.Web;

public static class ServiceCollectionMockExtensions
{
    public static IServiceCollection ReplaceServiceWithSingletonMock<TService>(this IServiceCollection services)
        where TService : class
    {
        var service = services.FirstOrDefault(d => d.ServiceType == typeof(TService));
        services.Remove(service);

        services.AddSingleton(_ => Substitute.For<TService>());
        return services;
    }
}
