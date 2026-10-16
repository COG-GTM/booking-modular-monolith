using Booking;
using Booking.Booking.Features.CreatingBook.V1;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Caching;
using BuildingBlocks.Constants;
using BuildingBlocks.Core;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.Logging;
using BuildingBlocks.Validation;
using BuildingBlocks.Web;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Unit.Test.Extensions;

public class InfrastructureExtensionsTests
{
    [Fact]
    public void add_booking_modules_registers_module_services()
    {
        var builder = CreateBuilder();

        builder.AddBookingModules();

        builder
            .Services.Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IEventMapper) && descriptor.ImplementationFactory != null
            );
        builder.Services.Should().Contain(descriptor => descriptor.ServiceType == typeof(BookingEventMapper));
        builder
            .Services.Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IValidator<CreateBooking>)
                && descriptor.ImplementationType == typeof(CreateBookingValidator)
            );
        builder
            .Services.Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IProjectionProcessor)
                && descriptor.ImplementationType == typeof(BookingProjection)
            );
        builder
            .Services.Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IEventStoreDBRepository<>)
                && descriptor.ImplementationType == typeof(EventStoreDBRepository<>)
            );
        builder
            .Services.Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IRequestHandler<CreateBooking, CreateBookingResult>)
                && descriptor.ImplementationType == typeof(CreateBookingCommandHandler)
            );

        var expectedBehaviors = new[]
        {
            typeof(ValidationBehavior<,>),
            typeof(LoggingBehavior<,>),
            typeof(CachingBehavior<,>),
            typeof(InvalidateCachingBehavior<,>),
        };
        var registeredBehaviors = builder
            .Services.Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();
        registeredBehaviors.Should().ContainInOrder(expectedBehaviors);
    }

    [Fact]
    public void add_custom_mediatr_registers_pipeline_behaviors_in_order()
    {
        var services = new ServiceCollection();

        services.AddCustomMediatR();

        services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .Select(descriptor => descriptor.ImplementationType)
            .Should()
            .ContainInOrder(
                typeof(ValidationBehavior<,>),
                typeof(LoggingBehavior<,>),
                typeof(CachingBehavior<,>),
                typeof(InvalidateCachingBehavior<,>)
            );
    }

    [Fact]
    public void booking_background_switch_controls_event_store_subscription_registration()
    {
        var enabledBuilder = CreateBuilder();
        enabledBuilder.AddBookingModules();
        enabledBuilder
            .Services.Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationFactory != null
            );

        var disabledBuilder = CreateBuilder(
            new Dictionary<string, string?> { ["Modules:Booking:BackgroundProcessingEnabled"] = "false" }
        );
        disabledBuilder.AddBookingModules();
        disabledBuilder
            .Services.Should()
            .NotContain(descriptor =>
                descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationFactory != null
            );
    }

    [Fact]
    public async Task use_booking_modules_returns_same_application()
    {
        var builder = CreateBuilder();
        builder.AddBookingModules();
        var app = builder.Build();

        try
        {
            app.UseBookingModules().Should().BeSameAs(app);
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    [Fact]
    public async Task minimal_endpoint_maps_authorized_create_booking_post_route()
    {
        var builder = CreateBuilder();
        builder.Services.AddCustomVersioning();
        builder.AddBookingModules();
        var app = builder.Build();

        try
        {
            app.MapMinimalEndpoints();

            var endpoint = ((IEndpointRouteBuilder)app)
                .DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .Single(candidate =>
                    candidate.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "CreateBooking"
                );

            endpoint.RoutePattern.RawText.Should().Contain("booking");
            endpoint
                .Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Should()
                .Contain(data => data.Policy == IdentityConstant.Policy.ApiScope);
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Should().Contain("POST");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static WebApplicationBuilder CreateBuilder(Dictionary<string, string?>? overrides = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "test" });
        var values = new Dictionary<string, string?>
        {
            ["EventStoreOptions:ConnectionString"] = "esdb://localhost:2113?tls=false",
            ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
            ["MongoOptions:Booking:DatabaseName"] = "booking_test",
            ["Grpc:FlightAddress"] = "http://localhost:5201",
            ["Grpc:PassengerAddress"] = "http://localhost:5301",
        };
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }
        builder.Configuration.AddInMemoryCollection(values);
        return builder;
    }
}
