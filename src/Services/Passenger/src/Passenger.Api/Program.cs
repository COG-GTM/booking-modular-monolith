using BuildingBlocks.Core;
using BuildingBlocks.Exception;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;
using Passenger;
using Passenger.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddJwt();
builder.Services.AddScoped<ICurrentUserProvider, CurrentUserProvider>();
builder.Services.AddTransient<AuthHeaderHandler>();
builder.AddPersistMessageProcessor();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddControllers();
builder.Services.AddAspnetOpenApi();
builder.Services.AddCustomVersioning();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IEventDispatcher, EventDispatcher>();

builder.Services.AddCustomMassTransit(builder.Environment, TransportType.RabbitMq, typeof(PassengerRoot).Assembly);

builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcExceptionInterceptor>();
});

builder.Services.AddEasyCaching(options =>
{
    options.UseInMemory(builder.Configuration, "mem");
});
builder.Services.AddProblemDetails();

builder.Services.AddScoped<IEventMapper>(sp => sp.GetRequiredService<PassengerEventMapper>());

builder.AddPassengerModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UsePassengerModules();

app.UseServiceDefaults();
app.UseCustomProblemDetails();
app.UseCorrelationId();

var appOptions = app.Configuration.GetOptions<AppOptions>(nameof(AppOptions));
app.MapGet("/", x => x.Response.WriteAsync(appOptions.Name));

if (app.Environment.IsDevelopment())
{
    app.UseAspnetOpenApi();
}

app.MapMinimalEndpoints();

app.Run();

namespace Passenger.Api
{
    public partial class Program { }
}
