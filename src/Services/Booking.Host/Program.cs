using Booking.Extensions.Infrastructure;
using Booking.Host.Extensions;
using BuildingBlocks.Web;

var builder = WebApplication.CreateBuilder(args);

builder.AddBookingHostInfrastructure();
builder.AddBookingModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.UseBookingModules();
app.UseBookingHostInfrastructure();
app.MapMinimalEndpoints();

app.Run();

namespace Booking.Host
{
    public partial class Program { }
}
