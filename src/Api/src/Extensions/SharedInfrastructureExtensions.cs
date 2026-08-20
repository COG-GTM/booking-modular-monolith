using Booking;
using BuildingBlocks.Core;
using Flight;
using Identity;
using Passenger;

namespace Api.Extensions;

public static class SharedInfrastructureExtensions
{
    public static WebApplicationBuilder AddSharedInfrastructure(this WebApplicationBuilder builder)
    {
        return builder.AddSharedInfrastructure(services =>
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
        });
    }

    public static WebApplication UserSharedInfrastructure(this WebApplication app)
    {
        return app.UseSharedInfrastructure();
    }
}
