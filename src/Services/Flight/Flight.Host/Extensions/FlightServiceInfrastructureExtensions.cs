using BuildingBlocks.Core;
using BuildingBlocks.EFCore;
using BuildingBlocks.Grpc;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Mongo;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Flight;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using RabbitMQ.Client;

namespace Flight.Host.Extensions;

public static class FlightServiceInfrastructureExtensions
{
    public static WebApplicationBuilder AddFlightServiceInfrastructure(this WebApplicationBuilder builder)
    {
        builder.AddServiceDefaults();

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddTransient<AuthHeaderHandler>();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddControllers();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();

        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            TransportType.RabbitMq,
            typeof(FlightRoot).Assembly
        );

        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<GrpcExceptionInterceptor>();
        });
        builder.Services.AddGrpcHealthService();

        builder.Services.AddEasyCaching(options =>
        {
            options.UseInMemory(builder.Configuration, "mem");
        });
        builder.Services.AddProblemDetails();

        builder
            .Services.AddHealthChecks()
            .AddNpgSql(
                sp => sp.GetRequiredService<IConfiguration>().GetPostgresConnectionString(nameof(Flight)),
                name: "flight-postgres",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(10)
            )
            .AddMongoDb(
                sp => new MongoClient(
                    sp.GetRequiredService<IOptionsMonitor<MongoOptions>>().Get(nameof(Flight)).ConnectionString
                ),
                name: "flight-mongo",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"],
                timeout: TimeSpan.FromSeconds(10)
            )
            .AddRabbitMQ(
                serviceProvider =>
                {
                    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
                    var connectionString = configuration.GetConnectionString("rabbitmq");
                    ConnectionFactory factory;

                    if (!string.IsNullOrEmpty(connectionString))
                    {
                        factory = new ConnectionFactory { Uri = new Uri(connectionString) };
                    }
                    else
                    {
                        var rabbitMqOptions = configuration.GetOptions<RabbitMqOptions>(nameof(RabbitMqOptions));
                        factory = new ConnectionFactory
                        {
                            HostName = rabbitMqOptions.HostName,
                            Port = rabbitMqOptions.Port ?? 5672,
                            UserName = rabbitMqOptions.UserName,
                            Password = rabbitMqOptions.Password,
                            VirtualHost = "/",
                        };
                    }

                    return factory.CreateConnectionAsync();
                },
                name: "flight-rabbitmq",
                tags: ["ready"]
            );

        return builder;
    }

    public static WebApplication UseFlightServiceInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseServiceDefaults();
        app.UseCustomProblemDetails();
        app.UseCorrelationId();

        app.MapGet("/", response => response.Response.WriteAsync(appOptions.Name));
        app.MapGrpcHealthService();

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
