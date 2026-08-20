using Booking.Extensions.Infrastructure;
using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UserSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace BookingService
{
    public partial class Program { }
}
