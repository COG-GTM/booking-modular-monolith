using Booking;
using BuildingBlocks.Core;
using Flight;
using Identity;
using Microsoft.Extensions.DependencyInjection;
using Passenger;

namespace Api.Extensions;

public static class ApiEventMapperExtensions
{
    public static IServiceCollection AddApiEventMappers(this IServiceCollection services)
    {
        services.AddScoped<IEventMapper>(sp =>
        {
            var mappers = new IEventMapper[]
            {
                sp.GetRequiredService<FlightEventMapper>(),
                sp.GetRequiredService<IdentityEventMapper>(),
                sp.GetRequiredService<PassengerEventMapper>(),
                sp.GetRequiredService<BookingEventMapper>(),
            };

            return new CompositeEventMapper(mappers);
        });

        return services;
    }
}
