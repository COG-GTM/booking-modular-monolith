using System.Reflection;
using BuildingBlocks.Web;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.MassTransit;

using Exception;

public static class Extensions
{
    public const string RabbitMqConnectionName = "rabbitmq";

    // Transport comes from MessagingOptions:Transport (default RabbitMq). Only the given assemblies are
    // scanned for consumers/sagas/activities, so each host binds just its own module's queues.
    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment env,
        Action<IBusRegistrationContext, IBusFactoryConfigurator>? configureBus,
        params Assembly[] consumerAssemblies
    )
    {
        var transportType = ResolveTransport(configuration);

        return services.AddCustomMassTransit(env, transportType, configureBus, consumerAssemblies);
    }

    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        TransportType transportType,
        params Assembly[] consumerAssemblies
    ) => services.AddCustomMassTransit(env, transportType, configureBus: null, consumerAssemblies);

    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        TransportType transportType,
        Action<IBusRegistrationContext, IBusFactoryConfigurator>? configureBus,
        params Assembly[] consumerAssemblies
    )
    {
        services.AddValidateOptions<RabbitMqOptions>();
        services.AddValidateOptions<MessagingOptions>();

        if (env.IsEnvironment("test"))
        {
            services.AddMassTransitTestHarness(configure =>
            {
                SetupMasstransitConfigurations(configure, transportType, configureBus, consumerAssemblies);
            });
        }
        else
        {
            services.AddMassTransit(configure =>
            {
                SetupMasstransitConfigurations(configure, transportType, configureBus, consumerAssemblies);
            });
        }

        return services;
    }

    public static TransportType ResolveTransport(IConfiguration configuration)
    {
        var configured = configuration[$"{nameof(MessagingOptions)}:{nameof(MessagingOptions.Transport)}"];

        if (string.IsNullOrWhiteSpace(configured))
            return TransportType.RabbitMq;

        if (Enum.TryParse<TransportType>(configured, ignoreCase: true, out var transport) && Enum.IsDefined(transport))
            return transport;

        throw new InvalidOperationException(
            $"Unsupported {nameof(MessagingOptions)}:{nameof(MessagingOptions.Transport)} '{configured}'. "
                + $"Use one of: {string.Join(", ", Enum.GetNames<TransportType>())}."
        );
    }

    // ConnectionStrings:rabbitmq (amqp URI, set by Aspire) wins over RabbitMqOptions.
    public static void ConfigureRabbitMqHost(IRabbitMqBusFactoryConfigurator configurator, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(RabbitMqConnectionName);

        if (!string.IsNullOrEmpty(connectionString))
        {
            configurator.Host(new Uri(connectionString));
            return;
        }

        var rabbitMqOptions = configuration.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));

        if (string.IsNullOrWhiteSpace(rabbitMqOptions?.HostName))
        {
            throw new InvalidOperationException(
                $"RabbitMQ transport needs ConnectionStrings:{RabbitMqConnectionName} or {nameof(RabbitMqOptions)}:{nameof(RabbitMqOptions.HostName)}."
            );
        }

        configurator.Host(
            rabbitMqOptions.HostName,
            rabbitMqOptions.Port ?? 5672,
            string.IsNullOrWhiteSpace(rabbitMqOptions.VirtualHost) ? "/" : rabbitMqOptions.VirtualHost,
            h =>
            {
                h.Username(rabbitMqOptions.UserName);
                h.Password(rabbitMqOptions.Password);
            }
        );
    }

    private static void SetupMasstransitConfigurations(
        IBusRegistrationConfigurator configure,
        TransportType transportType,
        Action<IBusRegistrationContext, IBusFactoryConfigurator>? configureBus,
        Assembly[] consumerAssemblies
    )
    {
        configure.SetEndpointNameFormatter(ModuleEndpointNameFormatter.Instance);

        // MassTransit scans every loaded assembly when given an empty list.
        var assemblies = consumerAssemblies?.Where(a => a is not null).Distinct().ToArray() ?? [];

        if (assemblies.Length > 0)
        {
            configure.AddConsumers(assemblies);
            configure.AddSagaStateMachines(assemblies);
            configure.AddSagas(assemblies);
            configure.AddActivities(assemblies);
        }

        switch (transportType)
        {
            case TransportType.RabbitMq:
                configure.UsingRabbitMq(
                    (context, configurator) =>
                    {
                        ConfigureRabbitMqHost(configurator, context.GetRequiredService<IConfiguration>());

                        configurator.UseMessageRetry(AddRetryConfiguration);
                        configureBus?.Invoke(context, configurator);
                        configurator.ConfigureEndpoints(context);
                    }
                );

                break;
            case TransportType.InMemory:
                configure.UsingInMemory(
                    (context, configurator) =>
                    {
                        configurator.UseMessageRetry(AddRetryConfiguration);
                        configureBus?.Invoke(context, configurator);
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
