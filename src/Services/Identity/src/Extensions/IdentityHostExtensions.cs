using BuildingBlocks.Core;
using BuildingBlocks.Core.Event;
using BuildingBlocks.EFCore;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Figgle.Fonts;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;

namespace Identity.Host.Extensions;

public static class IdentityHostExtensions
{
    public static WebApplicationBuilder AddIdentityHostInfrastructure(this WebApplicationBuilder builder)
    {
        var appOptions = builder.Services.GetOptions<AppOptions>(nameof(AppOptions));
        Console.WriteLine(FiggleFonts.Standard.Render(appOptions.Name));

        builder.AddServiceDefaults();

        builder
            .Services.AddHealthChecks()
            .AddNpgSql(
                serviceProvider =>
                    serviceProvider.GetRequiredService<IConfiguration>().GetPostgresConnectionString("Identity"),
                name: "identity-postgres",
                tags: ["ready"]
            );

        if (builder.Configuration.GetValue<TransportType?>("MessageBroker:TransportType") == TransportType.RabbitMq)
        {
            builder
                .Services.AddHealthChecks()
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
                    name: "identity-rabbitmq",
                    tags: ["ready"]
                );
        }

        builder.Services.AddJwt();
        builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddAspnetOpenApi();
        builder.Services.AddCustomVersioning();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IEventHeadersProvider, HttpContextEventHeadersProvider>();
        builder.Services.AddCustomMassTransit(
            builder.Configuration,
            builder.Environment,
            assembly: [typeof(global::Identity.IdentityRoot).Assembly]
        );
        builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);
        builder.Services.AddEasyCaching(options => options.UseInMemory(builder.Configuration, "mem"));
        builder.Services.AddProblemDetails();

        return builder;
    }

    public static WebApplication UseIdentityHostInfrastructure(this WebApplication app)
    {
        var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));

        app.UseServiceDefaults();
        if (!app.Environment.IsDevelopment())
        {
            app.MapHealthChecks("/health");
            app.MapHealthChecks("/alive", new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        }

        app.UseCustomProblemDetails();
        app.UseCorrelationId();
        app.MapGet("/", () => appOptions.Name);

        if (app.Environment.IsDevelopment())
        {
            app.UseAspnetOpenApi();
        }

        return app;
    }
}
