using System.Reflection;
using BuildingBlocks.Web;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.MassTransit;

using Exception;

public static class Extensions
{
    // Aspire injects the broker as ConnectionStrings:rabbitmq; it takes precedence over MessageBroker:RabbitMq.
    public const string RabbitMqConnectionName = "rabbitmq";

    // Transport and connection come from the "MessageBroker" configuration section (defaults to InMemory).
    // Only TransportType is read at registration; everything else is read from the final IConfiguration
    // when the bus is built, so late configuration sources (e.g. WebApplicationFactory overrides) apply.
    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env,
        params Assembly[] assemblies
    )
    {
        var transportType = configuration
            .GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName)
            .TransportType;

        return services.AddCustomMassTransit(
            env,
            transportType,
            sp =>
                sp.GetRequiredService<IConfiguration>()
                    .GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName),
            assemblies
        );
    }

    // Explicit transport, e.g. InMemory for unit tests.
    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        TransportType transportType,
        params Assembly[] assemblies
    )
    {
        return services.AddCustomMassTransit(
            env,
            new MessageBrokerOptions { TransportType = transportType },
            assemblies
        );
    }

    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        MessageBrokerOptions options,
        params Assembly[] assemblies
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        return services.AddCustomMassTransit(env, options.TransportType, _ => options, assemblies);
    }

    public static IEndpointNameFormatter CreateEndpointNameFormatter(MessageBrokerOptions options)
    {
        var serviceName = options.ServiceName?.Trim().Trim('-');

        return string.IsNullOrEmpty(serviceName)
            ? KebabCaseEndpointNameFormatter.Instance
            : new KebabCaseEndpointNameFormatter($"{serviceName}-", includeNamespace: false);
    }

    private static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        TransportType transportType,
        Func<IServiceProvider, MessageBrokerOptions> optionsFactory,
        Assembly[] assemblies
    )
    {
        services.TryAddSingleton(sp => optionsFactory(sp) with { TransportType = transportType });

        if (env.IsEnvironment("test"))
        {
            services.AddMassTransitTestHarness(configure =>
            {
                SetupMasstransitConfigurations(configure, transportType, assemblies);
            });
        }
        else
        {
            services.AddMassTransit(configure =>
            {
                SetupMasstransitConfigurations(configure, transportType, assemblies);
            });
        }

        services.Replace(
            ServiceDescriptor.Singleton(sp =>
                CreateEndpointNameFormatter(sp.GetRequiredService<MessageBrokerOptions>())
            )
        );

        return services;
    }

    private static void SetupMasstransitConfigurations(
        IBusRegistrationConfigurator configure,
        TransportType transportType,
        Assembly[] assemblies
    )
    {
        // MassTransit scans every loaded assembly when given an empty list, so only register explicit assemblies.
        var explicitAssemblies = assemblies?.Where(a => a is not null).Distinct().ToArray() ?? [];

        if (explicitAssemblies.Length > 0)
        {
            configure.AddConsumers(explicitAssemblies);
            configure.AddSagaStateMachines(explicitAssemblies);
            configure.AddSagas(explicitAssemblies);
            configure.AddActivities(explicitAssemblies);
        }

        switch (transportType)
        {
            case TransportType.RabbitMq:
                configure.UsingRabbitMq(
                    (context, configurator) =>
                    {
                        var options = context.GetRequiredService<MessageBrokerOptions>();
                        var connectionString = context
                            .GetService<IConfiguration>()
                            ?.GetConnectionString(RabbitMqConnectionName);

                        if (!string.IsNullOrEmpty(connectionString))
                        {
                            configurator.Host(new Uri(connectionString));
                        }
                        else
                        {
                            var rabbitMq = options.RabbitMq ?? new RabbitMqTransportOptions();

                            configurator.Host(
                                rabbitMq.Host,
                                rabbitMq.Port,
                                rabbitMq.VirtualHost,
                                h =>
                                {
                                    h.Username(rabbitMq.Username);
                                    h.Password(rabbitMq.Password);
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
                        ConfigureBus(context, configurator, context.GetRequiredService<MessageBrokerOptions>());
                    }
                );

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transportType), transportType, message: null);
        }
    }

    private static void ConfigureBus<TEndpointConfigurator>(
        IBusRegistrationContext context,
        IBusFactoryConfigurator<TEndpointConfigurator> configurator,
        MessageBrokerOptions options
    )
        where TEndpointConfigurator : IReceiveEndpointConfigurator
    {
        configurator.UseMessageRetry(AddRetryConfiguration);

        if (options.UseInbox)
        {
            configurator.UseConsumeFilter(typeof(ConsumeFilter<>), context);
        }

        configurator.ConfigureEndpoints(context, CreateEndpointNameFormatter(options));
    }

    private static void AddRetryConfiguration(IRetryConfigurator retryConfigurator)
    {
        retryConfigurator
            .Exponential(3, TimeSpan.FromMilliseconds(200), TimeSpan.FromMinutes(120), TimeSpan.FromMilliseconds(200))
            .Ignore<ValidationException>(); // don't retry if we have invalid data and message goes to _error queue masstransit
    }
}
