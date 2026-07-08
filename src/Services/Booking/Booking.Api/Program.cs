using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.MassTransit;
using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddServiceInfrastructure<BookingEventMapper>(TransportType.RabbitMq, typeof(BookingRoot).Assembly);

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UseServiceDefaults();
app.UseServiceInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
