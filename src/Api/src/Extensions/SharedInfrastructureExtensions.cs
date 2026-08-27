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
        Shared.Hosting.SharedInfrastructureExtensions.AddSharedInfrastructure(builder);

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
        return Shared.Hosting.SharedInfrastructureExtensions.UseSharedInfrastructure(app);
    }
}
