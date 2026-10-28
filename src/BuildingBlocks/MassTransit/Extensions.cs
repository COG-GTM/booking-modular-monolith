using System.Reflection;
using BuildingBlocks.Exception;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BuildingBlocks.MassTransit;

public static class Extensions
{
    public static IServiceCollection AddCustomMassTransit(
        this IServiceCollection services,
        IWebHostEnvironment env,
        IConfiguration configuration,
        string serviceName,
        params Assembly[] consumerAssemblies
    )
    {
        var transport =
            configuration.GetValue<TransportType?>("Messaging:Transport")
            ?? (env.IsEnvironment("test") ? TransportType.InMemory : TransportType.RabbitMq);
        if (
            transport == TransportType.InMemory
            && !env.IsEnvironment("test")
            && !(env.IsDevelopment() && configuration.GetValue<bool>("Messaging:AllowInMemory"))
        )
            throw new InvalidOperationException(
                "InMemory messaging requires the test environment or Development with Messaging:AllowInMemory=true."
            );

        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        services.AddScoped(typeof(ConsumeFilter<>));

        void Configure(IBusRegistrationConfigurator registration)
        {
            registration.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, false));
            // An empty assembly list makes MassTransit scan every loaded assembly.
            if (consumerAssemblies.Length > 0)
                registration.AddConsumers(consumerAssemblies.Distinct().ToArray());

            if (transport == TransportType.RabbitMq)
            {
                registration.UsingRabbitMq(
                    (context, bus) =>
                    {
                        var connection = configuration.GetConnectionString("rabbitmq");
                        if (!string.IsNullOrWhiteSpace(connection))
                            bus.Host(new Uri(connection));
                        else
                        {
                            var options =
                                configuration.GetSection(nameof(RabbitMqOptions)).Get<RabbitMqOptions>() ?? new();
                            bus.Host(
                                options.HostName,
                                options.Port,
                                options.VirtualHost,
                                host =>
                                {
                                    host.Username(options.UserName);
                                    host.Password(options.Password);
                                }
                            );
                        }

                        bus.UseMessageRetry(AddRetryConfiguration);
                        bus.UseConsumeFilter(typeof(ConsumeFilter<>), context);
                        bus.ConfigureEndpoints(context);
                    }
                );
            }
            else if (transport == TransportType.InMemory)
            {
                registration.UsingInMemory(
                    (context, bus) =>
                    {
                        bus.UseMessageRetry(AddRetryConfiguration);
                        bus.UseConsumeFilter(typeof(ConsumeFilter<>), context);
                        bus.ConfigureEndpoints(context);
                    }
                );
            }
            else
                throw new ArgumentOutOfRangeException(nameof(transport));
        }

        if (env.IsEnvironment("test"))
            services.AddMassTransitTestHarness(Configure);
        else
            services.AddMassTransit(Configure);

        return services;
    }

    private static void AddRetryConfiguration(IRetryConfigurator retry) =>
        retry
            .Exponential(3, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(200))
            .Ignore<ValidationException>();
}
