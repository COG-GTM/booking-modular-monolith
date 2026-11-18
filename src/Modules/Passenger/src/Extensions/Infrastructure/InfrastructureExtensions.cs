using BuildingBlocks.EFCore;
using BuildingBlocks.Mapster;
using BuildingBlocks.Mongo;
using BuildingBlocks.Web;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Passenger.Data;
using Passenger.GrpcServer.Services;
using Passenger.Passengers.Features.CompletingRegisterPassenger.V1;

namespace Passenger.Extensions.Infrastructure;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddPassengerModules(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<PassengerEventMapper>();
        builder.AddMinimalEndpoints(assemblies: typeof(PassengerRoot).Assembly);
        builder.Services.AddValidatorsFromAssembly(typeof(PassengerRoot).Assembly);
        builder.Services.AddCustomMapster(typeof(PassengerRoot).Assembly);
        builder.AddCustomDbContext<PassengerDbContext>(nameof(Passenger));
        builder.AddMongoDbContext<PassengerReadDbContext>();

        builder.Services.AddCustomMediatR();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(CompleteRegisterPassengerEndpoint.RateLimitPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    CompleteRegisterPassengerEndpoint.GetRateLimitPartitionKey(httpContext),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = CompleteRegisterPassengerEndpoint.RateLimitPermitsPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return builder;
    }


    public static WebApplication UsePassengerModules(this WebApplication app)
    {
        app.UseMigration<PassengerDbContext>();
        app.UseRateLimiter();
        app.MapGrpcService<PassengerGrpcServices>();

        return app;
    }
}