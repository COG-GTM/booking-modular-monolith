using System.Net.Security;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using BuildingBlocks.EventStoreDB.BackgroundWorkers;
using BuildingBlocks.EventStoreDB.Projections;
using BuildingBlocks.EventStoreDB.Repository;
using BuildingBlocks.EventStoreDB.Subscriptions;
using EventStore.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.EventStoreDB;

using Web;

public class EventStoreOptions
{
    public string ConnectionString { get; set; } = default!;

    // Used when the connection string has no user info.
    public string? Username { get; set; }
    public string? Password { get; set; }

    // PEM CA certificate used to validate the EventStoreDB node certificate.
    public string? TlsCaFile { get; set; }
}


public static class EventStoreTlsValidator
{
    public static bool Validate(X509Certificate? certificate, SslPolicyErrors errors, X509Certificate2 caCertificate)
    {
        if (errors == SslPolicyErrors.None)
            return true;

        if (certificate == null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) ||
            errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(caCertificate);

        using var serverCertificate = new X509Certificate2(certificate);
        return chain.Build(serverCertificate);
    }
}

public record EventStoreDBOptions(
    bool UseInternalCheckpointing = true
);

public static class EventStoreDBConfigExtensions
{
    public static IServiceCollection AddEventStoreDB(this IServiceCollection services, IConfiguration configuration,
        EventStoreDBOptions? options = null)
    {

        services
            .AddSingleton(x =>
            {
                var aspireConnectionString = configuration.GetConnectionString("eventstore");
                var eventStoreOptions = services.GetOptions<EventStoreOptions>(nameof(EventStoreOptions));
                var settings = EventStoreClientSettings.Create(aspireConnectionString ?? eventStoreOptions.ConnectionString);

                if (settings.DefaultCredentials == null &&
                    !string.IsNullOrEmpty(eventStoreOptions.Username) &&
                    !string.IsNullOrEmpty(eventStoreOptions.Password))
                {
                    settings.DefaultCredentials = new UserCredentials(eventStoreOptions.Username, eventStoreOptions.Password);
                }

                if (!string.IsNullOrEmpty(eventStoreOptions.TlsCaFile))
                {
                    var caCertificate = X509Certificate2.CreateFromPem(File.ReadAllText(eventStoreOptions.TlsCaFile));

                    settings.CreateHttpMessageHandler = () => new SocketsHttpHandler
                    {
                        EnableMultipleHttp2Connections = true,
                        SslOptions = new SslClientAuthenticationOptions
                        {
                            RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                                EventStoreTlsValidator.Validate(certificate, errors, caCertificate)
                        }
                    };
                }

                return new EventStoreClient(settings);
            })
            .AddScoped(typeof(IEventStoreDBRepository<>), typeof(EventStoreDBRepository<>))
            .AddTransient<EventStoreDBSubscriptionToAll, EventStoreDBSubscriptionToAll>();

        if (options?.UseInternalCheckpointing != false)
            services.AddTransient<ISubscriptionCheckpointRepository, EventStoreDBSubscriptionCheckpointRepository>();

        return services;
    }

    public static IServiceCollection AddEventStoreDBSubscriptionToAll(
        this IServiceCollection services,
        EventStoreDBSubscriptionToAllOptions? subscriptionOptions = null,
        bool checkpointToEventStoreDB = true)
    {
        if (checkpointToEventStoreDB)
            services.AddTransient<ISubscriptionCheckpointRepository, EventStoreDBSubscriptionCheckpointRepository>();

        return services.AddHostedService(serviceProvider =>
            {
                var logger =
                    serviceProvider.GetRequiredService<ILogger<BackgroundWorker>>();

                var eventStoreDBSubscriptionToAll =
                    serviceProvider.GetRequiredService<EventStoreDBSubscriptionToAll>();

                return new BackgroundWorker(
                    logger,
                    ct =>
                        eventStoreDBSubscriptionToAll.SubscribeToAll(
                            subscriptionOptions ?? new EventStoreDBSubscriptionToAllOptions(),
                            ct
                        )
                );
            }
        );
    }

    public static IServiceCollection AddProjections(this IServiceCollection services,
        params Assembly[] assembliesToScan)
    {
        services.AddSingleton<IProjectionPublisher, ProjectionPublisher>();

        RegisterProjections(services, assembliesToScan!);

        return services;
    }

    private static void RegisterProjections(IServiceCollection services, Assembly[] assembliesToScan)
    {
        services.Scan(scan => scan
            .FromAssemblies(assembliesToScan)
            .AddClasses(classes => classes.AssignableTo<IProjectionProcessor>()) // Filter classes
            .AsImplementedInterfaces()
            .WithTransientLifetime());
    }
}