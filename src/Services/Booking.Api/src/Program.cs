using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceHostInfrastructure<BookingEventMapper>(typeof(BookingRoot).Assembly);
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
