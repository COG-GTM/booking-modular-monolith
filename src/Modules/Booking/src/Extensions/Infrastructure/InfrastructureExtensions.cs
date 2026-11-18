using Booking.Data;
using BuildingBlocks.EventStoreDB;
using BuildingBlocks.EventStoreDB.Events;
using BuildingBlocks.EventStoreDB.Subscriptions;
using EventStore.Client;
using BuildingBlocks.Mapster;
using BuildingBlocks.Mongo;
using BuildingBlocks.Web;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Booking.Extensions.Infrastructure;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddBookingModules(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<BookingEventMapper>();
        builder.AddMinimalEndpoints(assemblies: typeof(BookingRoot).Assembly);
        builder.Services.AddValidatorsFromAssembly(typeof(BookingRoot).Assembly);
        builder.Services.AddCustomMapster(typeof(BookingRoot).Assembly);
        builder.AddMongoDbContext<BookingReadDbContext>();

        // ref: https://github.com/oskardudycz/EventSourcing.NetCore/tree/main/Sample/EventStoreDB/ECommerce
        builder.Services.AddEventStore(builder.Configuration, typeof(BookingRoot).Assembly)
            .AddEventStoreDBSubscriptionToAll(new EventStoreDBSubscriptionToAllOptions
            {
                FilterOptions = new SubscriptionFilterOptions(
                    StreamFilter.Prefix(StreamNameMapper.ToStreamPrefix<global::Booking.Booking.Models.Booking>()))
            });

        builder.Services.AddGrpcClients();

        builder.Services.AddCustomMediatR();

        return builder;
    }


    public static WebApplication UseBookingModules(this WebApplication app)
    {
        return app;
    }
}