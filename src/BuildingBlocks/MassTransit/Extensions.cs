using System.Reflection;
using BuildingBlocks.Web;
using Humanizer;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.MassTransit;

using Exception;

public static class Extensions
{
    // Monolith entry point: the transport is fixed by the caller (in-memory) and consumers are not wrapped by the inbox filter.
    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        TransportType transportType,
        params Assembly[] assembly
    )
    {
        var options = new MessageBrokerOptions { TransportType = transportType, UseInbox = false };

        return services.AddCustomMassTransit(env, options, assembly);
    }

    // Service-host entry point: the transport is read from the "MessageBrokerOptions" configuration section.
    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env,
        params Assembly[] assembly
    )
    {
        services.AddValidateOptions<MessageBrokerOptions>();

        var options = configuration.GetOptions<MessageBrokerOptions>(nameof(MessageBrokerOptions));

        return services.AddCustomMassTransit(env, options, assembly);
    }

    private static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        MessageBrokerOptions options,
        params Assembly[] assembly
    )
    {
        services.AddValidateOptions<RabbitMqOptions>();

        if (env.IsEnvironment("test"))
        {
            services.AddMassTransitTestHarness(configure =>
            {
                SetupMasstransitConfigurations(services, configure, options, assembly);
            });
        }
        else
        {
            services.AddMassTransit(configure =>
            {
                SetupMasstransitConfigurations(services, configure, options, assembly);
            });
        }

        return services;
    }

    private static void SetupMasstransitConfigurations(
        IServiceCollection services,
        IBusRegistrationConfigurator configure,
        MessageBrokerOptions options,
        params Assembly[] assembly
    )
    {
        // MassTransit scans the whole AppDomain when given an empty list, so only scan when assemblies are provided.
        if (assembly.Length > 0)
        {
            configure.AddConsumers(assembly);
            configure.AddSagaStateMachines(assembly);
            configure.AddSagas(assembly);
            configure.AddActivities(assembly);
        }

        if (!string.IsNullOrWhiteSpace(options.ServiceName))
        {
            configure.SetEndpointNameFormatter(
                new KebabCaseEndpointNameFormatter(options.ServiceName.Kebaberize(), includeNamespace: false)
            );
        }

        switch (options.TransportType)
        {
            case TransportType.RabbitMq:
                configure.UsingRabbitMq(
                    (context, configurator) =>
                    {
                        var configuration = context.GetRequiredService<IConfiguration>();

                        var aspireConnectionString = configuration.GetConnectionString("rabbitmq");

                        if (!string.IsNullOrEmpty(aspireConnectionString))
                        {
                            configurator.Host(new Uri(aspireConnectionString));
                        }
                        else
                        {
                            var rabbitMqOptions = services.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));

                            ArgumentNullException.ThrowIfNull(rabbitMqOptions);

                            configurator.Host(
                                rabbitMqOptions?.HostName,
                                rabbitMqOptions?.Port ?? 5672,
                                "/",
                                h =>
                                {
                                    h.Username(rabbitMqOptions.UserName);
                                    h.Password(rabbitMqOptions.Password);
                                }
                            );
                        }

                        ConfigureBus(context, configurator, options);
                    }
                );

                break;
            case TransportType.InMemory:
                configure.UsingInMemory(
                    (context, configurator) =>
                    {
                        ConfigureBus(context, configurator, options);
                    }
                );

                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(options.TransportType),
                    options.TransportType,
                    message: null
                );
        }
    }

    private static void ConfigureBus<TEndpointConfigurator>(
        IBusRegistrationContext context,
        IBusFactoryConfigurator<TEndpointConfigurator> configurator,
        MessageBrokerOptions options
    )
        where TEndpointConfigurator : IReceiveEndpointConfigurator
    {
        if (options.UseInbox)
        {
            configurator.UseConsumeFilter(typeof(ConsumeFilter<>), context);
        }

        configurator.ConfigureEndpoints(context);
        configurator.UseMessageRetry(AddRetryConfiguration);
    }

    private static void AddRetryConfiguration(IRetryConfigurator retryConfigurator)
    {
        retryConfigurator
            .Exponential(3, TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(120), TimeSpan.FromMilliseconds(200))
            .Ignore<ValidationException>(); // don't retry if we have invalid data and message goes to _error queue masstransit
    }
}
