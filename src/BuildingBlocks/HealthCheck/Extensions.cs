using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;
using HealthChecks.UI.Client;
using Humanizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace BuildingBlocks.HealthCheck;

public static class Extensions
{
    public const string HealthEndpointPath = "/health";
    public const string LivenessEndpointPath = "/health/live";
    public const string ReadinessEndpointPath = "/health/ready";
    public const string AlivenessEndpointPath = "/alive";

    public const string LiveTag = "live";
    public const string ReadyTag = "ready";
    public const string SelfCheckName = "self";

    private const string PostgresConnectionKey = "PostgresOptions:ConnectionString";
    private const string PersistMessageConnectionKey = "PersistMessageOptions:ConnectionString";
    private const string MongoConnectionKey = "MongoOptions:ConnectionString";
    private const string EventStoreConnectionKey = "EventStoreOptions:ConnectionString";
    private const string MongoAspireConnectionName = "mongo";
    private const string PersistMessageSuffix = "-persist-message";

    private static readonly TimeSpan DependencyTimeout = TimeSpan.FromSeconds(5);

    // Liveness = the process answers ("live" tag). Readiness = every registered check, i.e. self plus
    // the data stores/broker from configuration and any check other components add (e.g. Booking's gRPC
    // dependency checks). Check names are fixed from configuration at registration; connection strings
    // are re-read from the final IConfiguration when a check runs.
    public static IServiceCollection AddCustomHealthCheck(this IServiceCollection services)
    {
        var configuration = services.BuildServiceProvider().GetService<IConfiguration>();
        var healthOptions = configuration?.GetOptions<HealthOptions>(nameof(HealthOptions)) ?? new HealthOptions();

        var healthChecksBuilder = services.AddHealthChecks();

        // Idempotent: hosts and ServiceDefaults may both call this, and duplicate check names throw.
        if (services.Any(d => d.ServiceType == typeof(CustomHealthCheckMarker)))
            return services;

        services.AddSingleton<CustomHealthCheckMarker>();
        healthChecksBuilder.AddCheck(SelfCheckName, () => HealthCheckResult.Healthy(), [LiveTag]);

        if (configuration is not null && healthOptions.DependencyChecksEnabled)
            healthChecksBuilder.AddDependencyChecks(configuration);

        if (healthOptions.Enabled)
        {
            var appOptions = configuration?.GetOptions<AppOptions>(nameof(AppOptions)) ?? new AppOptions();

            services
                .AddHealthChecksUI(setup =>
                {
                    setup.SetEvaluationTimeInSeconds(60);
                    setup.AddHealthCheckEndpoint($"Self Check - {appOptions.Name}", ReadinessEndpointPath);
                })
                .AddInMemoryStorage();
        }

        return services;
    }

    public static WebApplication UseCustomHealthCheck(this WebApplication app)
    {
        var healthOptions = app.Configuration.GetOptions<HealthOptions>(nameof(HealthOptions));

        app.MapCustomHealthChecks();

        if (healthOptions.Enabled)
            app.MapHealthChecksUI(options => options.UIPath = "/health-ui");

        return app;
    }

    // Probes are mapped in every environment (compose/Kubernetes call them) and bypass authorization.
    public static IEndpointRouteBuilder MapCustomHealthChecks(this IEndpointRouteBuilder endpoints)
    {
        var liveness = new HealthCheckOptions { Predicate = r => r.Tags.Contains(LiveTag) };
        var readiness = new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
        };

        endpoints.MapHealthChecks(LivenessEndpointPath, liveness).AllowAnonymous();
        endpoints.MapHealthChecks(ReadinessEndpointPath, readiness).AllowAnonymous();

        // Kept for the Aspire AppHost / existing compose probes.
        endpoints.MapHealthChecks(HealthEndpointPath).AllowAnonymous();
        endpoints.MapHealthChecks(AlivenessEndpointPath, liveness).AllowAnonymous();

        return endpoints;
    }

    public static bool IsHealthEndpoint(PathString path) =>
        path.StartsWithSegments(HealthEndpointPath, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(AlivenessEndpointPath, StringComparison.OrdinalIgnoreCase);

    private static IHealthChecksBuilder AddDependencyChecks(
        this IHealthChecksBuilder builder,
        IConfiguration configuration
    )
    {
        string[] readyTags = [ReadyTag];

        foreach (var (name, resolve) in GetPostgresConnections(configuration))
        {
            builder.AddNpgSql(
                sp => resolve(sp.GetRequiredService<IConfiguration>())!,
                name: name,
                failureStatus: HealthStatus.Unhealthy,
                tags: readyTags,
                timeout: DependencyTimeout
            );
        }

        if (!string.IsNullOrEmpty(GetMongoConnectionString(configuration)))
        {
            builder.AddMongoDb(
                clientFactory: sp => new MongoClient(GetMongoConnectionString(sp.GetRequiredService<IConfiguration>())),
                name: "mongodb",
                failureStatus: HealthStatus.Unhealthy,
                tags: readyTags,
                timeout: DependencyTimeout
            );
        }

        if (!string.IsNullOrEmpty(configuration[EventStoreConnectionKey]))
        {
            builder.Add(
                new HealthCheckRegistration(
                    "eventstore",
                    sp => new EventStoreDbHealthCheck(() => sp.GetRequiredService<IConfiguration>()[EventStoreConnectionKey]),
                    HealthStatus.Unhealthy,
                    readyTags,
                    DependencyTimeout
                )
            );
        }

        var messageBroker = configuration.GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName);
        if (messageBroker.TransportType == TransportType.RabbitMq)
        {
            builder.AddRabbitMQ(
                sp => CreateRabbitMqConnectionFactory(sp).CreateConnectionAsync(),
                name: "rabbitmq",
                failureStatus: HealthStatus.Unhealthy,
                tags: readyTags,
                timeout: DependencyTimeout
            );
        }

        return builder;
    }

    // PostgresOptions:ConnectionString is either a single string (monolith) or one entry per module
    // (service hosts); every module also has its own outbox/inbox store. Each check resolves the same
    // connection the module uses: Aspire ConnectionStrings:<module>[-persist-message] first, then the options.
    public static IEnumerable<(string Name, Func<IConfiguration, string?> Resolve)> GetPostgresConnections(
        IConfiguration configuration
    )
    {
        if (!string.IsNullOrEmpty(configuration[PostgresConnectionKey]))
            yield return ("postgres", c => c[PostgresConnectionKey]);

        foreach (var module in configuration.GetSection(PostgresConnectionKey).GetChildren())
        {
            var aspireName = module.Key.Kebaberize();
            var optionsKey = module.Path;

            if (!string.IsNullOrEmpty(configuration.GetConnectionString(aspireName) ?? module.Value))
                yield return ($"postgres-{aspireName}", c => c.GetConnectionString(aspireName) ?? c[optionsKey]);
        }

        var persistModules = configuration
            .GetSection(PersistMessageConnectionKey)
            .GetChildren()
            .Select(m => m.Key.Kebaberize())
            .Concat(
                configuration
                    .GetSection("ConnectionStrings")
                    .GetChildren()
                    .Where(c => c.Key.EndsWith(PersistMessageSuffix, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Key[..^PersistMessageSuffix.Length].ToLowerInvariant())
            )
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var module in persistModules)
        {
            var aspireName = module + PersistMessageSuffix;
            var optionsKey = $"{PersistMessageConnectionKey}:{module}";

            if (!string.IsNullOrEmpty(configuration.GetConnectionString(aspireName) ?? configuration[optionsKey]))
                yield return ($"postgres-{aspireName}", c => c.GetConnectionString(aspireName) ?? c[optionsKey]);
        }
    }

    // Same precedence as BuildingBlocks.Mongo: Aspire ConnectionStrings:mongo, then MongoOptions:ConnectionString.
    public static string? GetMongoConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(MongoAspireConnectionName) ?? configuration[MongoConnectionKey];

    // Same precedence as the MassTransit bus: Aspire ConnectionStrings:rabbitmq, then MessageBroker:RabbitMq.
    private static ConnectionFactory CreateRabbitMqConnectionFactory(IServiceProvider serviceProvider)
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString(MassTransit.Extensions.RabbitMqConnectionName);

        if (!string.IsNullOrEmpty(connectionString))
        {
            return new ConnectionFactory { Uri = new Uri(connectionString) };
        }

        var options =
            serviceProvider.GetService<MessageBrokerOptions>()
            ?? configuration.GetOptions<MessageBrokerOptions>(MessageBrokerOptions.SectionName);
        var rabbitMq = options.RabbitMq ?? new RabbitMqTransportOptions();

        return new ConnectionFactory
        {
            HostName = rabbitMq.Host,
            Port = rabbitMq.Port,
            VirtualHost = rabbitMq.VirtualHost,
            UserName = rabbitMq.Username,
            Password = rabbitMq.Password,
        };
    }

    private sealed class CustomHealthCheckMarker;
}
