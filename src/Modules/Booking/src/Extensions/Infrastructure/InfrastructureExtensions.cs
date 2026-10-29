using Booking.Data;
using BuildingBlocks.Core;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.EventStoreDB.Subscriptions;
using BuildingBlocks.Mapster;
using BuildingBlocks.Mongo;
using BuildingBlocks.Web;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Extensions.Infrastructure;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddBookingModules(this WebApplicationBuilder builder)
    {
        builder.Services.AddEventMapper<BookingEventMapper>();
        builder.AddMinimalEndpoints(assemblies: typeof(BookingRoot).Assembly);
        builder.Services.AddValidatorsFromAssembly(typeof(BookingRoot).Assembly);
        builder.Services.AddCustomMapster(typeof(BookingRoot).Assembly);
        builder.AddMongoDbContext<BookingReadDbContext>("Booking");

        // ref: https://github.com/oskardudycz/EventSourcing.NetCore/tree/main/Sample/EventStoreDB/ECommerce
        builder.Services.AddEventStore(builder.Configuration, typeof(BookingRoot).Assembly);

        if (builder.Configuration.GetValue<bool?>("Modules:Booking:BackgroundProcessingEnabled") != false)
        {
            var subscriptionOptions = new EventStoreDBSubscriptionToAllOptions
            {
                SubscriptionId = builder.Configuration["Modules:Booking:SubscriptionId"] ?? "default",
            };
            builder.Services.AddSingleton(subscriptionOptions);
            builder.Services.AddEventStoreDBSubscriptionToAll(subscriptionOptions);
        }

        builder.Services.AddGrpcClients();

        builder.Services.AddCustomMediatR();

        return builder;
    }

    public static WebApplication UseBookingModules(this WebApplication app)
    {
        return app;
    }
}
