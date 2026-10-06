using Humanizer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace BuildingBlocks.Mongo
{
    using Web;

    public static class Extensions
    {
        /// <summary>
        /// Registers a module-owned Mongo read database. Options are resolved per <paramref name="connectionName"/>:
        /// <c>MongoOptions</c> supplies shared defaults (server), <c>MongoOptions:{ConnectionName}</c> overrides them
        /// (at least <c>DatabaseName</c>), and an Aspire <c>ConnectionStrings:{connection-name}-read</c> wins over both.
        /// </summary>
        public static IServiceCollection AddMongoDbContext<TContext>(
            this WebApplicationBuilder builder, string connectionName)
        where TContext : MongoDbContext
        {
            return builder.Services.AddMongoDbContext<TContext, TContext>(builder.Configuration, connectionName);
        }

        public static IServiceCollection AddMongoDbContext<TContextService, TContextImplementation>(
            this IServiceCollection services, IConfiguration configuration, string connectionName)
        where TContextService : IMongoDbContext
        where TContextImplementation : MongoDbContext, TContextService
        {
            ArgumentException.ThrowIfNullOrEmpty(connectionName);

            var aspireConnectionName = $"{connectionName.Kebaberize()}-read";

            services.AddOptions<MongoOptions>(connectionName)
                .Bind(configuration.GetSection(nameof(MongoOptions)))
                .Bind(configuration.GetSection($"{nameof(MongoOptions)}:{connectionName}"))
                .PostConfigure(options =>
                               {
                                   // a module-specific Aspire database resource supplies server and database name;
                                   // the shared Aspire server resource only ever supplies the server
                                   var moduleConnectionString = configuration.GetConnectionString(aspireConnectionName);

                                   if (moduleConnectionString is not null)
                                   {
                                       options.ConnectionString = moduleConnectionString;
                                       options.DatabaseName =
                                           MongoUrl.Create(moduleConnectionString).DatabaseName ?? options.DatabaseName;

                                       return;
                                   }

                                   var serverConnectionString = configuration.GetConnectionString("mongo");

                                   if (serverConnectionString is not null)
                                   {
                                       options.ConnectionString = serverConnectionString;
                                   }
                               })
                .Validate(
                    options => !string.IsNullOrEmpty(options.ConnectionString),
                    $"{nameof(MongoOptions)}:{nameof(MongoOptions.ConnectionString)} is required.")
                .Validate(
                    options => !string.IsNullOrEmpty(options.DatabaseName),
                    $"{nameof(MongoOptions)}:{connectionName}:{nameof(MongoOptions.DatabaseName)} is required.")
                .ValidateOnStart();

            services.AddScoped(
                typeof(TContextImplementation),
                sp =>
                {
                    var options = sp.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(connectionName);

                    return ActivatorUtilities.CreateInstance<TContextImplementation>(sp, Options.Create(options));
                });
            if (typeof(TContextService) != typeof(TContextImplementation))
            {
                services.AddScoped(typeof(TContextService), sp => sp.GetRequiredService<TContextImplementation>());
            }

            services.AddTransient(typeof(IMongoUnitOfWork<>), typeof(MongoUnitOfWork<>));

            return services;
        }
    }
}
