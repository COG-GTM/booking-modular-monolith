using BuildingBlocks.Caching;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.Logging;
using BuildingBlocks.Mapster;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Validation;
using FluentAssertions;
using MapsterMapper;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Passenger;
using Passenger.Data;
using Passenger.Extensions.Infrastructure;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;
using Passenger.Passengers.Features.GettingPassengerById.V1;
using Xunit;

namespace Unit.Test.Extensions;

public class PassengerExtensionTests
{
    [Fact]
    public void Add_custom_mediatr_registers_pipeline_behaviors_and_passenger_handlers()
    {
        var services = new ServiceCollection();
        services.AddCustomMediatR();
        var behaviorDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IPipelineBehavior<,>))
            .ToList();

        behaviorDescriptors
            .Select(descriptor => descriptor.ImplementationType)
            .Should()
            .Equal(
                typeof(ValidationBehavior<,>),
                typeof(LoggingBehavior<,>),
                typeof(EfTxPassengerBehavior<,>),
                typeof(CachingBehavior<,>),
                typeof(InvalidateCachingBehavior<,>)
            );
        behaviorDescriptors
            .Select(descriptor => descriptor.Lifetime)
            .Should()
            .Equal(
                ServiceLifetime.Transient,
                ServiceLifetime.Transient,
                ServiceLifetime.Scoped,
                ServiceLifetime.Transient,
                ServiceLifetime.Transient
            );
        services
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType
                == typeof(IRequestHandler<CompleteRegisterPassenger, CompleteRegisterPassengerResult>)
            );
        services
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IRequestHandler<GetPassengerById, GetPassengerByIdResult>)
            );
        services
            .Should()
            .Contain(descriptor =>
                descriptor.ServiceType == typeof(IRequestHandler<CompleteRegisterPassengerMongoCommand, MediatR.Unit>)
            );
    }

    [Fact]
    public void Add_passenger_modules_registers_validators_and_module_infrastructure()
    {
        var settings = new Dictionary<string, string?>
        {
            ["PostgresOptions:ConnectionString:Passenger"] = "Host=localhost;Database=passenger",
            ["PersistMessageOptions:ConnectionString:Passenger"] = "Host=localhost;Database=persist_message",
            ["MongoOptions:ConnectionString"] = "mongodb://localhost:27017",
            ["MongoOptions:Passenger:DatabaseName"] = "passenger_read",
            ["PersistMessageOptions:Enabled"] = "false",
            ["Modules:Passenger:BackgroundProcessingEnabled"] = "false",
        };
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings);

        builder.AddPassengerModules();

        var services = builder.Services;
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(CompleteRegisterPassengerValidator));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(GetPassengerByIdValidator));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IEventMapper));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(PassengerEventMapper));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(PassengerDbContext));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(PassengerReadDbContext));
        services.Should().Contain(descriptor => descriptor.ServiceType == typeof(IMapper));
    }
}
