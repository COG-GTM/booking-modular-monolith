using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Humanizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BuildingBlocks.PersistMessageProcessor;

public static class Extensions
{
    private static readonly object StoreInitializationLock = new();

    public static IServiceCollection AddPersistMessageProcessor<TModule>(
        this WebApplicationBuilder builder,
        string moduleName
    )
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        builder.Services.AddValidateOptions<PersistMessageOptions>();

        builder.Services.AddDbContext<PersistMessageDbContext<TModule>>(
            (sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                var name = moduleName.Kebaberize();
                var connectionString =
                    configuration.GetConnectionString($"{name}-persist-message")
                    ?? configuration[$"PersistMessageOptions:ConnectionString:{moduleName}"]
                    ?? configuration.GetConnectionString("persist-message")
                    ?? configuration["PersistMessageOptions:ConnectionString"];
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException(
                        $"No persist-message or Postgres connection string configured for module '{moduleName}'."
                    );

                options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention();
                options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            }
        );

        builder.Services.AddScoped<IPersistMessageDbContext<TModule>>(sp =>
        {
            var context = sp.GetRequiredService<PersistMessageDbContext<TModule>>();
            lock (StoreInitializationLock)
            {
                context.Database.EnsureCreated();
                context.CreatePersistMessageTableIfNotExists();
            }
            return context;
        });
        builder.Services.AddScoped<IPersistMessageProcessor<TModule>, PersistMessageProcessor<TModule>>();
        builder.Services.TryAddScoped<
            IIntegrationEventPublisher<TModule>,
            PersistMessageIntegrationEventPublisher<TModule>
        >();
        builder.Services.TryAddSingleton<PersistMessageWorkerStores>();
        if (
            builder.Configuration.GetValue<bool?>($"Modules:{moduleName}:BackgroundProcessingEnabled") != false
            && builder.Configuration.GetValue<bool?>("PersistMessageOptions:Enabled") != false
        )
        {
            builder.Services.AddHostedService<PersistMessageBackgroundService<TModule>>();
        }

        return builder.Services;
    }
}
