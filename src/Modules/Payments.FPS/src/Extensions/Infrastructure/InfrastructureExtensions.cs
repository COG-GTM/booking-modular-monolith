using BuildingBlocks.EFCore;
using BuildingBlocks.Mapster;
using BuildingBlocks.Mongo;
using BuildingBlocks.Web;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Payments.FPS.Data;
using Payments.FPS.Data.Seed;

namespace Payments.FPS.Extensions.Infrastructure;

public static class InfrastructureExtensions
{
    public static WebApplicationBuilder AddPaymentsFpsModules(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<PaymentsFpsEventMapper>();
        builder.AddMinimalEndpoints(assemblies: typeof(PaymentsFpsRoot).Assembly);
        builder.Services.AddValidatorsFromAssembly(typeof(PaymentsFpsRoot).Assembly);
        builder.Services.AddCustomMapster(typeof(PaymentsFpsRoot).Assembly);
        builder.AddCustomDbContext<PaymentsDbContext>("Payments");
        builder.Services.AddScoped<IDataSeeder, PaymentsDataSeeder>();
        builder.AddMongoDbContext<PaymentsReadDbContext>();
        builder.Services.AddCustomMediatR();
        return builder;
    }

    public static WebApplication UsePaymentsFpsModules(this WebApplication app)
    {
        app.UseMigration<PaymentsDbContext>();
        return app;
    }
}
