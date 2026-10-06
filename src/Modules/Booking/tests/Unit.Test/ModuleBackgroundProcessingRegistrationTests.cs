using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.EventStoreDB.BackgroundWorkers;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.PersistMessageProcessor;
using EventStore.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Unit.Test;

public class ModuleBackgroundProcessingRegistrationTests
{
    [Fact]
    public void registers_booking_worker_and_all_stream_subscription_by_default()
    {
        var services = CreateModuleServices();
        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();

        hostedServices.Should().Contain(d =>
            d.ImplementationType == typeof(PersistMessageBackgroundService<BookingRoot>)
        );
        hostedServices.Should().Contain(d =>
            d.ImplementationFactory != null
            && d.ImplementationFactory.Method.ReturnType == typeof(BackgroundWorker)
        );
    }

    [Fact]
    public void skips_booking_background_services_when_background_processing_is_disabled()
    {
        var services = CreateModuleServices(backgroundProcessingEnabled: false);
        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();

        hostedServices.Should().NotContain(d =>
            d.ImplementationType == typeof(PersistMessageBackgroundService<BookingRoot>)
        );
        hostedServices.Should().NotContain(d =>
            d.ImplementationFactory != null
            && d.ImplementationFactory.Method.ReturnType == typeof(BackgroundWorker)
        );
        services.Should().Contain(d => d.ServiceType == typeof(EventStoreClient));
        services.Should().Contain(d => d.ServiceType == typeof(IEventStoreDBRepository<>));
    }

    private static IServiceCollection CreateModuleServices(bool backgroundProcessingEnabled = true)
    {
        var builder = WebApplication.CreateBuilder();
        if (!backgroundProcessingEnabled)
        {
            builder.Configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Modules:Booking:BackgroundProcessingEnabled"] = "false" }
            );
        }

        builder.AddBookingModules();
        return builder.Services;
    }
}
