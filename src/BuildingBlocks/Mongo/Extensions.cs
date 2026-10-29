using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Mongo;

public static class Extensions
{
    public static IServiceCollection AddMongoDbContext<TContext>(this WebApplicationBuilder builder, string moduleName)
        where TContext : MongoDbContext
    {
        builder
            .Services.AddOptions<MongoOptions>(moduleName)
            .Configure<IConfiguration>(
                (options, configuration) =>
                {
                    options.DatabaseName = configuration[$"MongoOptions:{moduleName}:DatabaseName"]!;
                    options.ConnectionString =
                        configuration[$"MongoOptions:{moduleName}:ConnectionString"]
                        ?? configuration.GetConnectionString("mongo")
                        ?? configuration["MongoOptions:ConnectionString"]!;
                }
            )
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DatabaseName),
                $"MongoOptions:{moduleName}:DatabaseName is required."
            )
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                $"A Mongo connection string is required for module '{moduleName}'."
            );
        builder.Services.AddScoped<TContext>();
        return builder.Services;
    }
}
