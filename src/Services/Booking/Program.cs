using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Web;
using Shared.ServiceHost;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure(typeof(BookingRoot).Assembly);

builder.Services.AddEventMapper<BookingEventMapper>();

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace BookingService
{
    public partial class Program { }
}
