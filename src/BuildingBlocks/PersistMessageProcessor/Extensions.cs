using System.Reflection;
using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EFCore;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.PersistMessageProcessor;

public static class Extensions
{
    /// <summary>
    /// Registers a module-owned outbox/inbox store. The persist_message table lives in the module's own
    /// database (resolved from <paramref name="connectionName"/>, the same name used for the module's write
    /// DbContext), together with the module's processor, event dispatcher and background dispatcher.
    /// </summary>
    public static IServiceCollection AddPersistMessageProcessor<TModule>(
        this WebApplicationBuilder builder,
        string connectionName
    )
        where TModule : class
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        if (builder.Services.All(x => x.ServiceType != typeof(PersistMessageOptions)))
        {
            builder.Services.AddValidateOptions<PersistMessageOptions>();
        }

        builder.Services.AddDbContext<PersistMessageDbContext<TModule>>(
            (sp, options) =>
            {
                var connectionString = builder.Configuration.GetPostgresConnectionString(connectionName);

                options.UseNpgsql(
                        connectionString,
                        dbOptions =>
                        {
                            dbOptions.MigrationsAssembly(
                                typeof(PersistMessageDbContext<TModule>).Assembly.GetName().Name);
                        })
                    // https://github.com/efcore/EFCore.NamingConventions
                    .UseSnakeCaseNamingConvention();

                // Todo: follow up the issues of .net 9 to use better approach taht will provide by .net!
                options.ConfigureWarnings(
                    w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            });

        builder.Services.AddScoped<IPersistMessageDbContext<TModule>>(
            provider =>
            {
                var persistMessageDbContext =
                    provider.GetRequiredService<PersistMessageDbContext<TModule>>();

                persistMessageDbContext.Database.EnsureCreated();
                persistMessageDbContext.CreatePersistMessageTableIfNotExists();

                return persistMessageDbContext;
            });

        builder.Services.AddScoped<IPersistMessageProcessor<TModule>, PersistMessageProcessor<TModule>>();
        builder.Services.AddScoped<IIntegrationEventPublisher>(
            provider =>
                new PersistMessageIntegrationEventPublisher(
                    provider.GetRequiredService<IPersistMessageProcessor<TModule>>()));

        // Lets module-agnostic infrastructure (e.g. the MassTransit inbox filter) find the store owned by a
        // consumer's module through its assembly.
        builder.Services.AddKeyedScoped<IPersistMessageProcessor>(
            typeof(TModule).Assembly,
            (provider, _) => provider.GetRequiredService<IPersistMessageProcessor<TModule>>());

        builder.Services.AddScoped<IEventDispatcher<TModule>, EventDispatcher<TModule>>();

        builder.Services.AddHostedService<PersistMessageBackgroundService<TModule>>();

        return builder.Services;
    }

    public static IPersistMessageProcessor GetPersistMessageProcessor(
        this IServiceProvider serviceProvider,
        Assembly moduleAssembly
    )
    {
        return serviceProvider.GetRequiredKeyedService<IPersistMessageProcessor>(moduleAssembly);
    }
}
