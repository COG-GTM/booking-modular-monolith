using BuildingBlocks.Web;
using Booking;
using Booking.Extensions.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHostInfrastructure(typeof(BookingRoot).Assembly);
builder.Services.AddModuleEventMapper<BookingEventMapper>();

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UseServiceHostInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
