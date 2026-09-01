using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Core;
using BuildingBlocks.Jwt;
using BuildingBlocks.MassTransit;
using BuildingBlocks.OpenApi;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.ProblemDetails;
using BuildingBlocks.Web;
using Microsoft.AspNetCore.Mvc;

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

builder.Services.AddCustomMassTransit(builder.Environment, TransportType.RabbitMq, typeof(BookingRoot).Assembly);

builder.Services.Configure<ApiBehaviorOptions>(options => options.SuppressModelStateInvalidFilter = true);

builder.Services.AddEasyCaching(options =>
{
    options.UseInMemory(builder.Configuration, "mem");
});
builder.Services.AddProblemDetails();

builder.Services.AddScoped<IEventMapper>(sp => sp.GetRequiredService<BookingEventMapper>());

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

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

namespace Booking.Api
{
    public partial class Program { }
}
