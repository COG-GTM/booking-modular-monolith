using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Humanizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.PersistMessageProcessor;

// Describes one module-owned outbox/inbox store (used by tests and diagnostics to enumerate stores).
public sealed record PersistMessageStore(string ModuleName, Type DbContextType);

public static class Extensions
{
    // Registers an outbox/inbox store owned by one module. Connection string resolution order:
    // Aspire "ConnectionStrings:<module>-persist-message", then "PersistMessageOptions:ConnectionString:<Module>".
    // The polling worker is skipped when PersistMessageOptions:Enabled is false or
    // Modules:<Module>:BackgroundProcessingEnabled is false.
    public static IServiceCollection AddPersistMessageProcessor<TModule>(
        this WebApplicationBuilder builder,
        string moduleName
    )
        where TModule : class
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);

        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        if (builder.Services.All(d => d.ServiceType != typeof(PersistMessageOptions)))
        {
            builder.Services.AddValidateOptions<PersistMessageOptions>();
        }

        builder.Services.AddDbContext<PersistMessageDbContext<TModule>>(
            (sp, options) =>
            {
                var connectionString = GetPersistMessageConnectionString(
                    sp.GetRequiredService<IConfiguration>(),
                    moduleName
                );

                options
                    .UseNpgsql(
                        connectionString,
                        dbOptions =>
                        {
                            dbOptions.MigrationsAssembly(typeof(PersistMessageDbContext).Assembly.GetName().Name);
                        }
                    )
                    // https://github.com/efcore/EFCore.NamingConventions
                    .UseSnakeCaseNamingConvention();

                // Todo: follow up the issues of .net 9 to use better approach taht will provide by .net!
                options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            }
        );

        builder.Services.AddScoped<IPersistMessageDbContext<TModule>>(provider =>
        {
            var persistMessageDbContext = provider.GetRequiredService<PersistMessageDbContext<TModule>>();

            persistMessageDbContext.Database.EnsureCreated();
            persistMessageDbContext.CreatePersistMessageTableIfNotExists();

            return persistMessageDbContext;
        });

        builder.Services.AddScoped<IPersistMessageProcessor<TModule>, PersistMessageProcessor<TModule>>();
        builder.Services.AddScoped<IEventDispatcher<TModule>, EventDispatcher<TModule>>();
        builder.Services.AddSingleton(new PersistMessageStore(moduleName, typeof(PersistMessageDbContext<TModule>)));

        var workerEnabled =
            builder.Configuration.GetValue(
                $"{nameof(PersistMessageOptions)}:{nameof(PersistMessageOptions.Enabled)}",
                true
            ) && builder.Configuration.IsModuleBackgroundProcessingEnabled(moduleName);

        if (workerEnabled)
        {
            builder.Services.AddHostedService<PersistMessageBackgroundService<TModule>>();
        }

        return builder.Services;
    }

    public static string GetPersistMessageConnectionString(IConfiguration configuration, string moduleName)
    {
        var connectionString =
            configuration.GetConnectionString($"{moduleName}-persist-message".Kebaberize())
            ?? configuration[$"{nameof(PersistMessageOptions)}:ConnectionString:{moduleName}"];

        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException(
                $"Persist message connection string for module '{moduleName}' not found. Set "
                    + $"'{nameof(PersistMessageOptions)}:ConnectionString:{moduleName}' or "
                    + $"'ConnectionStrings:{$"{moduleName}-persist-message".Kebaberize()}'."
            );
        }

        return connectionString;
    }
}
