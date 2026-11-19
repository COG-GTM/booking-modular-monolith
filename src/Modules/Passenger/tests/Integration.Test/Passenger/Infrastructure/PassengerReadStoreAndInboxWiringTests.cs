using BuildingBlocks.Contracts.EventBus.Messages;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.PersistMessageProcessor;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Passenger.Data;
using Passenger.Identity.Consumers.RegisteringNewUser.V1;
using Xunit;

namespace Integration.Test.Passenger.Infrastructure;

public class PassengerReadStoreAndInboxWiringTests
{
    [Fact]
    public void passenger_read_context_uses_passenger_named_mongo_database()
    {
        var builder = WebApplication.CreateBuilder(
            new WebApplicationOptions { EnvironmentName = "test", ContentRootPath = AppContext.BaseDirectory }
        );
        builder.Configuration["MongoOptions:Flight:DatabaseName"] = "flight_read";
        builder.Configuration["MongoOptions:Passenger:DatabaseName"] = "passenger_read";
        builder.Configuration["MongoOptions:ConnectionString"] = "mongodb://localhost:27017";
        builder.AddMongoDbContext<PassengerReadDbContext>("Passenger");
        using var provider = builder.Services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<PassengerReadDbContext>();

        Assert.Equal("passenger_read", context.Database.DatabaseNamespace.DatabaseName);
    }

    [Fact]
    public void registering_new_user_definition_limits_concurrency_and_adds_inbox_filter()
    {
        var endpointConfigurator = Substitute.For<IReceiveEndpointConfigurator>();
        var consumerConfigurator = Substitute.For<IConsumerConfigurator<RegisterNewUserHandler>>();
        var context = Substitute.For<IRegistrationContext>();
        new ExposedRegisterNewUserDefinition().Configure(endpointConfigurator, consumerConfigurator, context);

        Assert.Equal(1, endpointConfigurator.ConcurrentMessageLimit);
        var methodNames = endpointConfigurator.ReceivedCalls().Select(call => call.GetMethodInfo().Name).ToArray();
        Assert.Contains("ConnectConsumerConfigurationObserver", methodNames);
    }

    [Fact]
    public void passenger_inbox_filter_is_a_passenger_consume_filter()
    {
        Assert.True(
            typeof(PassengerInboxFilter<UserCreated>).IsAssignableTo(
                typeof(ConsumeFilter<global::Passenger.PassengerRoot, UserCreated>)
            )
        );
    }

    private sealed class ExposedRegisterNewUserDefinition : RegisterNewUserDefinition
    {
        public void Configure(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<RegisterNewUserHandler> consumerConfigurator,
            IRegistrationContext context
        ) => base.ConfigureConsumer(endpointConfigurator, consumerConfigurator, context);
    }
}
