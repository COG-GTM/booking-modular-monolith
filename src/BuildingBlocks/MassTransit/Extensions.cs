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
    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env,
        TransportType? transportType = null,
        params Assembly[] assembly
    )
    {
        services.AddValidateOptions<RabbitMqOptions>();

        var transport =
            transportType
            ?? configuration.GetValue<TransportType?>("MessageBroker:TransportType")
            ?? TransportType.RabbitMq;

        var serviceName = configuration.GetValue<string>("MessageBroker:ServiceName") ?? env.ApplicationName;

        if (env.IsEnvironment("test"))
        {
            services.AddMassTransitTestHarness(configure =>
            {
                SetupMasstransitConfigurations(services, configure, transport, serviceName, assembly);
            });
        }
        else
        {
            services.AddMassTransit(configure =>
            {
                SetupMasstransitConfigurations(services, configure, transport, serviceName, assembly);
            });
        }

        return services;
    }

    private static void SetupMasstransitConfigurations(
        IServiceCollection services,
        IBusRegistrationConfigurator configure,
        TransportType transportType,
        string serviceName,
        params Assembly[] assembly
    )
    {
        configure.AddConsumers(assembly);
        configure.AddSagaStateMachines(assembly);
        configure.AddSagas(assembly);
        configure.AddActivities(assembly);

        configure.SetEndpointNameFormatter(
            new KebabCaseEndpointNameFormatter(serviceName.Kebaberize(), includeNamespace: false)
        );

        switch (transportType)
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

                        configurator.UseMessageRetry(AddRetryConfiguration);

                        configurator.UseConsumeFilter(typeof(ConsumeFilter<>), context);

                        configurator.ConfigureEndpoints(context);
                    }
                );

                break;
            case TransportType.InMemory:
                configure.UsingInMemory(
                    (context, configurator) =>
                    {
                        configurator.UseMessageRetry(AddRetryConfiguration);

                        configurator.UseConsumeFilter(typeof(ConsumeFilter<>), context);

                        configurator.ConfigureEndpoints(context);
                    }
                );

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transportType), transportType, message: null);
        }
    }

    private static void AddRetryConfiguration(IRetryConfigurator retryConfigurator)
    {
        retryConfigurator
            .Exponential(3, TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(120), TimeSpan.FromMilliseconds(200))
            .Ignore<ValidationException>(); // don't retry if we have invalid data and message goes to _error queue masstransit
    }
}
