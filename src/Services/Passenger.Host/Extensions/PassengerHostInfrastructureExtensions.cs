using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.OpenApi;
using BuildingBlocks.OpenTelemetryCollector;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Grpc.AspNetCore.HealthChecks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Passenger;
using RabbitMQ.Client;

namespace Passenger.Host.Extensions;

public static class PassengerHostInfrastructureExtensions
{
    public static WebApplicationBuilder AddPassengerHostInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddCustomObservability();

        builder.Services.AddJwt();
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options =>
            {
                var metadata = builder.Configuration["Jwt:MetadataAddress"];
                if (!string.IsNullOrWhiteSpace(metadata))
                    options.MetadataAddress = metadata;
            }
        );
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventHeadersProvider, HttpEventHeadersProvider>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddCustomVersioning();
        if (builder.Environment.IsDevelopment())
            builder.Services.AddAspnetOpenApi();
        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();
        builder.Services.AddEventMapper<PassengerEventMapper>();

        builder.Services.AddCustomMassTransit(
            builder.Environment,
            builder.Configuration,
            "passenger-service",
            typeof(PassengerRoot).Assembly
        );

        builder.Services.AddGrpc(options => options.Interceptors.Add<GrpcExceptionInterceptor>());
        builder.Services.AddGrpcHealthChecks(options =>
        {
            options.Services.Map("", context => context.Tags.Contains("live"));
            options.Services.Map("passenger.PassengerGrpcService", context => context.Tags.Contains("live"));
        });

        AddPassengerHealthChecks(builder);

        return builder;
    }

    public static WebApplication UsePassengerHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseCustomProblemDetails();
        app.UseCorrelationId();
        app.UseCustomObservability();

        app.MapGrpcHealthChecksService();
        app.MapHealthChecks(
                "/health/live",
                new HealthCheckOptions { Predicate = check => check.Tags.Contains("live") }
            )
            .AllowAnonymous();
        app.MapHealthChecks("/health/ready").AllowAnonymous();
        app.MapGet("/", context => context.Response.WriteAsync(appOptions.Name));

        if (app.Environment.IsDevelopment())
            app.UseAspnetOpenApi();

        return app;
    }

    private static void AddPassengerHealthChecks(WebApplicationBuilder builder)
    {
        var healthChecks = builder.Services.AddHealthChecks();
        healthChecks.AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        healthChecks.AddNpgSql(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<IConfiguration>()
                    .ResolvePostgresConnectionString("Passenger"),
            name: "postgres-passenger",
            tags: ["ready"]
        );
        healthChecks.AddNpgSql(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<IConfiguration>()
                    .ResolvePersistMessageConnectionString("Passenger"),
            name: "postgres-passenger-persist-message",
            tags: ["ready"]
        );
        healthChecks.AddMongoDb(
            clientFactory: serviceProvider =>
                new MongoClient(
                    serviceProvider.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get("Passenger").ConnectionString
                ),
            name: "mongo-passenger",
            tags: ["ready"]
        );

        var transport =
            builder.Configuration.GetValue<TransportType?>("Messaging:Transport")
            ?? (builder.Environment.IsEnvironment("test") ? TransportType.InMemory : TransportType.RabbitMq);
        if (transport == TransportType.RabbitMq)
        {
            healthChecks.AddRabbitMQ(
                serviceProvider =>
                {
                    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                    var connectionString = configuration.GetConnectionString("rabbitmq");
                    var factory = new ConnectionFactory();
                    if (!string.IsNullOrWhiteSpace(connectionString))
                    {
                        factory.Uri = new Uri(connectionString);
                    }
                    else
                    {
                        var options = configuration.GetSection(nameof(RabbitMqOptions)).Get<RabbitMqOptions>() ?? new();
                        factory.HostName = options.HostName;
                        factory.Port = options.Port;
                        factory.VirtualHost = options.VirtualHost;
                        factory.UserName = options.UserName;
                        factory.Password = options.Password;
                    }

                    return factory.CreateConnectionAsync();
                },
                name: "rabbitmq",
                tags: ["ready"]
            );
        }
    }
}
