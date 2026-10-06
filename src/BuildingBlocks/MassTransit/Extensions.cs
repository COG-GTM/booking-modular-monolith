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
    /// <summary>
    /// Registers MassTransit using RabbitMQ as the transport. The broker is resolved from the Aspire
    /// <c>ConnectionStrings:rabbitmq</c> entry when present, otherwise from <see cref="RabbitMqOptions"/>.
    /// </summary>
    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        params Assembly[] assembly
    )
    {
        return services.AddCustomMassTransit(env, TransportType.RabbitMq, assembly);
    }

    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        TransportType transportType,
        params Assembly[] assembly
    )
    {
        services.AddValidateOptions<RabbitMqOptions>();

        if (env.IsEnvironment("test"))
        {
            services.AddMassTransitTestHarness(
                configure =>
                {
                    SetupMasstransitConfigurations(services, configure, transportType, assembly);
                });
        }
        else
        {
            services.AddMassTransit(
                configure =>
                {
                    SetupMasstransitConfigurations(services, configure, transportType, assembly);
                });
        }

        return services;
    }

    private static void SetupMasstransitConfigurations(
        IServiceCollection services,
        IBusRegistrationConfigurator configure,
        TransportType transportType,
        params Assembly[] assembly
    )
    {
        configure.AddConsumers(assembly);
        configure.AddSagaStateMachines(assembly);
        configure.AddSagas(assembly);
        configure.AddActivities(assembly);

        switch (transportType)
        {
            case TransportType.RabbitMq:
                configure.UsingRabbitMq(
                    (context, configurator) =>
                    {
                        var configuration = context.GetRequiredService<IConfiguration>();
                        var rabbitMqOptions = configuration.GetSection(nameof(RabbitMqOptions)).Get<RabbitMqOptions>();

                        var aspireConnectionString = configuration.GetConnectionString("rabbitmq");

                        if (!string.IsNullOrEmpty(aspireConnectionString))
                        {
                            configurator.Host(new Uri(aspireConnectionString));
                        }
                        else
                        {
                            ArgumentNullException.ThrowIfNull(rabbitMqOptions);
                            ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqOptions.HostName);

                            configurator.Host(
                                rabbitMqOptions.HostName,
                                rabbitMqOptions.Port ?? 5672,
                                "/",
                                h =>
                                {
                                    h.Username(rabbitMqOptions.UserName);
                                    h.Password(rabbitMqOptions.Password);
                                });
                        }

                        // Each service gets its own queue per consumer (e.g. "passenger-api-register-new-user"),
                        // so several hosts can consume the same integration event from the shared broker.
                        var queuePrefix = ResolveQueuePrefix(configuration, rabbitMqOptions);
                        configurator.ConfigureEndpoints(
                            context,
                            new KebabCaseEndpointNameFormatter(queuePrefix, includeNamespace: false));

                        configurator.UseMessageRetry(AddRetryConfiguration);
                    });

                break;
            case TransportType.InMemory:
                configure.UsingInMemory(
                    (context, configurator) =>
                    {
                        configurator.ConfigureEndpoints(context);
                        configurator.UseMessageRetry(AddRetryConfiguration);
                    });

                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(transportType),
                    transportType,
                    message: null);
        }
    }

    private static string ResolveQueuePrefix(IConfiguration configuration, RabbitMqOptions? rabbitMqOptions)
    {
        if (!string.IsNullOrWhiteSpace(rabbitMqOptions?.QueuePrefix))
        {
            return rabbitMqOptions.QueuePrefix;
        }

        var appName = configuration.GetSection(nameof(AppOptions)).Get<AppOptions>()?.Name;

        return string.IsNullOrWhiteSpace(appName) ? "service" : appName.Kebaberize();
    }

    private static void AddRetryConfiguration(IRetryConfigurator retryConfigurator)
    {
        retryConfigurator.Exponential(
                3,
                TimeSpan.FromMilliseconds(200),
                TimeSpan.FromMinutes(120),
                TimeSpan.FromMilliseconds(200))
            .Ignore<
                ValidationException>(); // don't retry if we have invalid data and message goes to _error queue masstransit
    }
}
