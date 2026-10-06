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
        builder.AddServiceHostInfrastructure(AppDomain.CurrentDomain.GetAssemblies());

        builder.Services.AddScoped<IEventMapper>(sp =>
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

        return builder;
    }

    public static WebApplication UserSharedInfrastructure(this WebApplication app)
    {
        return app.UseServiceHostInfrastructure();
    }
}
