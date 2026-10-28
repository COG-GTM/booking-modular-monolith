using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Mongo;

public static class Extensions
{
    public static IServiceCollection AddMongoDbContext<TContext>(this WebApplicationBuilder builder, string moduleName)
        where TContext : MongoDbContext
    {
        return builder.Services.AddMongoDbContext<TContext, TContext>(builder.Configuration, moduleName);
    }

    public static IServiceCollection AddMongoDbContext<TContextService, TContextImplementation>(
        this IServiceCollection services,
        IConfiguration configuration,
        string moduleName
    )
        where TContextService : class, IMongoDbContext
        where TContextImplementation : MongoDbContext, TContextService
    {
        services
            .AddOptions<MongoOptions>(moduleName)
            .Configure<IConfiguration>(
                (options, config) =>
                {
                    options.ConnectionString =
                        config[$"MongoOptions:{moduleName}:ConnectionString"]
                        ?? config.GetConnectionString("mongo")
                        ?? config["MongoOptions:ConnectionString"]!;
                    options.DatabaseName = config[$"MongoOptions:{moduleName}:DatabaseName"]!;
                }
            )
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "Mongo connection string is required."
            )
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DatabaseName),
                $"Mongo database name for '{moduleName}' is required."
            );
        services.AddScoped<TContextService, TContextImplementation>();
        services.AddTransient(typeof(IMongoUnitOfWork<>), typeof(MongoUnitOfWork<>));
        return services;
    }
}
