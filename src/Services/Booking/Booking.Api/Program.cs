using Booking;
using Booking.Extensions.Infrastructure;
using BuildingBlocks.Web;
using Shared.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddSharedInfrastructure();
builder.AddModuleEventMapper<BookingEventMapper>();

builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();

app.UseSharedInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Api
{
    public partial class Program { }
}
