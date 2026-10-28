using BuildingBlocks.Core;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.PersistMessageProcessor;

public static class Extensions
{
    public static string ResolvePersistMessageConnectionString(this IConfiguration configuration, string moduleName)
    {
        var connectionString =
            configuration[$"PersistMessageOptions:ConnectionString:{moduleName}"]
            ?? configuration.GetConnectionString($"{moduleName.ToLowerInvariant()}-persist-message");
        return !string.IsNullOrWhiteSpace(connectionString)
            ? connectionString
            : throw new ArgumentException($"Persist-message connection string '{moduleName}' not found.");
    }

    public static IServiceCollection AddPersistMessageProcessor<TModule>(
        this WebApplicationBuilder builder,
        string moduleName
    )
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        builder.Services.AddSingleton(new ModulePersistenceRegistration(moduleName, typeof(TModule)));
        builder.Services.Configure<PersistMessageOptions>(
            builder.Configuration.GetSection(nameof(PersistMessageOptions))
        );
        builder.Services.AddDbContext<PersistMessageDbContext<TModule>>(
            (sp, options) =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                options
                    .UseNpgsql(configuration.ResolvePersistMessageConnectionString(moduleName))
                    .UseSnakeCaseNamingConvention();
                options.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            }
        );
        builder.Services.AddScoped<IPersistMessageDbContext<TModule>>(sp =>
        {
            var context = sp.GetRequiredService<PersistMessageDbContext<TModule>>();
            context.Database.EnsureCreated();
            context.CreatePersistMessageTableIfNotExists();
            return context;
        });
        builder.Services.AddScoped<IPersistMessageProcessor<TModule>, PersistMessageProcessor<TModule>>();
        builder.Services.AddScoped<IEventPublisher<TModule>, PersistedEventPublisher<TModule>>();
        builder.Services.AddScoped<IEventDispatcher<TModule>>(sp => new ModuleEventDispatcher<TModule>(
            new EventDispatcher(
                sp.GetServices<IEventMapper>(),
                sp.GetRequiredService<IEventPublisher<TModule>>(),
                sp.GetRequiredService<IEventHeadersProvider>()
            )
        ));
        if (builder.Configuration.IsModuleBackgroundProcessingEnabled(moduleName))
            builder.Services.AddHostedService<PersistMessageBackgroundService<TModule>>();
        return builder.Services;
    }
}
