using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Mongo
{
    using Web;

    public static class Extensions
    {
        public static IServiceCollection AddMongoDbContext<TContext>(
            this WebApplicationBuilder builder, Action<MongoOptions>? configurator = null)
        where TContext : MongoDbContext
        {
            return builder.Services.AddMongoDbContext<TContext, TContext>(builder.Configuration, configurator);
        }

        // Registers a read DB owned by one module. Resolved from (first match wins):
        //   ConnectionString: MongoOptions:<Module>:ConnectionString, ConnectionStrings:<module>-mongo,
        //                     ConnectionStrings:mongo (Aspire), MongoOptions:ConnectionString
        //   DatabaseName:     MongoOptions:<Module>:DatabaseName (required, no shared fallback)
        public static IServiceCollection AddMongoDbContext<TContext>(
            this WebApplicationBuilder builder, string moduleName)
        where TContext : MongoDbContext
        {
            ArgumentException.ThrowIfNullOrEmpty(moduleName);

            var services = builder.Services;

            services.AddOptions<MongoOptions>(moduleName)
                .Configure<IConfiguration>((options, configuration) =>
                {
                    var rootSection = configuration.GetSection(nameof(MongoOptions));
                    var moduleSection = rootSection.GetSection(moduleName);

                    options.ConnectionString =
                        moduleSection[nameof(MongoOptions.ConnectionString)]
                        ?? configuration.GetConnectionString($"{moduleName}-mongo".Kebaberize())
                        ?? configuration.GetConnectionString("mongo")
                        ?? rootSection[nameof(MongoOptions.ConnectionString)]!;

                    options.DatabaseName = moduleSection[nameof(MongoOptions.DatabaseName)]!;
                })
                .Validate(
                    options => !string.IsNullOrEmpty(options.ConnectionString),
                    $"Mongo connection string for module '{moduleName}' not found.")
                .Validate(
                    options => !string.IsNullOrEmpty(options.DatabaseName),
                    $"'{nameof(MongoOptions)}:{moduleName}:{nameof(MongoOptions.DatabaseName)}' is required.");

            services.AddScoped(sp =>
            {
                var moduleOptions = sp.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(moduleName);
                return ActivatorUtilities.CreateInstance<TContext>(sp, Options.Create(moduleOptions));
            });

            services.AddScoped<IMongoDbContext>(sp => sp.GetRequiredService<TContext>());

            services.AddTransient(typeof(IMongoRepository<,>), typeof(MongoRepository<,>));
            services.AddTransient(typeof(IMongoUnitOfWork<>), typeof(MongoUnitOfWork<>));

            return services;
        }

        public static IServiceCollection AddMongoDbContext<TContextService, TContextImplementation>(
            this IServiceCollection services, IConfiguration configuration, Action<MongoOptions>? configurator = null)
        where TContextService : IMongoDbContext
        where TContextImplementation : MongoDbContext, TContextService
        {
            // Configure MongoOptions with Aspire-aware defaults
            services.AddOptions<MongoOptions>()
                .Bind(configuration.GetSection(nameof(MongoOptions)))
                .PostConfigure(options =>
                               {
                                   var aspireConnectionString = configuration.GetConnectionString("mongo");
                                   options.ConnectionString = aspireConnectionString ?? options.ConnectionString;
                               });

            if (configurator is { })
            {
                services.Configure(nameof(MongoOptions), configurator);
            }
            else
            {
                services.AddValidateOptions<MongoOptions>();
            }

            services.AddScoped(typeof(TContextService), typeof(TContextImplementation));
            services.AddScoped(typeof(TContextImplementation));

            services.AddScoped<IMongoDbContext>(sp => sp.GetRequiredService<TContextService>());

            services.AddTransient(typeof(IMongoRepository<,>), typeof(MongoRepository<,>));
            services.AddTransient(typeof(IMongoUnitOfWork<>), typeof(MongoUnitOfWork<>));

            return services;
        }
    }
}