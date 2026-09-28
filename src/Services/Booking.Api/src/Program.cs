using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Web;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHost(typeof(BookingRoot).Assembly, persistMessageConnectionName: "booking-persist-message");
builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();
app.UseServiceHost();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
