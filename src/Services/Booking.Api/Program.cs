using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Core;
using BuildingBlocks.MassTransit;
using BuildingBlocks.PersistMessageProcessor;
using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddMicroserviceInfrastructure();

builder.AddPersistMessageProcessor(connectionName: "booking-persist-message");

builder.Services.AddCustomMassTransit(builder.Environment, TransportType.RabbitMq, typeof(BookingRoot).Assembly);

builder.Services.AddScoped<IEventMapper, BookingEventMapper>();

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UseServiceDefaults();
app.UseMicroserviceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
